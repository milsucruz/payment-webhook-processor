# ServiceBusDeadLetterQueue

`src/Payments.Infrastructure/Messaging/ServiceBusDeadLetterQueue.cs`, registered in `src/Payments.Api/Program.cs`.

Implements the `IDeadLetterQueue` port (Application) over the dead-letter subqueue of `payment-events`. `ListDeadLetters` and `ReprocessDeadLetter` call it (see `docs/code/ReprocessDeadLetter.md`). Created in task 3.4. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## `PeekAsync`: list without removing

Peek reads messages without locking them and without changing `DeliveryCount`, so listing never interferes with a reprocess or with Service Bus itself (task 3.4: "without removing the messages").

- **A new receiver per call.** A receiver remembers where its last peek stopped, so a shared receiver would start the second listing after the end of the first and return nothing. A fresh receiver always starts at the oldest message.
- **A loop until `maxMessages` or an empty batch.** `PeekMessagesAsync` may return fewer messages than asked even when more exist, so one call is not enough.
- **No body in the result.** A body can be up to 256 KB; 100 of them would make a 25 MB response for a screen that shows the reason. The `DeadLetter` record carries the sequence number, the event ID (`MessageId`), reason, description, `DeliveryCount` and enqueue time.

## `ReprocessAsync`: why it receives a batch

Service Bus has no "receive the message with this ID". `ReceiveDeferredMessageAsync` takes a sequence number, but works only for messages that were deferred, not dead-lettered. This was a risk listed in section 14 of the architecture; task 3.4 confirmed it and uses the plan B written there:

1. Receive a batch of up to 50 messages from the dead-letter in peek-lock, waiting up to 2 s.
2. If the batch has the message with the requested sequence number, send a copy to `payment-events` and complete the original.
3. If not, keep the batch locked and receive the next one, so the same messages don't come back.
4. An empty batch means the message is not there (or another call holds its lock): return `null`, which the API turns into `404`.
5. In a `finally`, abandon every message that was received and is not the match, so they are free right away instead of after the 60 s lock.

The abandons use `CancellationToken.None`: if the request was aborted, the locks still have to be released.

### Why the sequence number and not the event ID

The `MessageId` is the `evt_` ID, and the same event can be in the dead-letter twice: a reprocessed poison message comes back after 5 more deliveries, and Stripe can resend an event that already failed. The sequence number is unique per message in the subqueue. The verification of task 3.4 showed it: the reprocessed event left as sequence number 1 and came back as 3.

### Send, then complete

The copy is sent before the original is completed. Basic has no transactions, so the two steps can't be atomic. In this order, a crash in the middle leaves the original in the dead-letter and a copy in the queue; the worker processes the copy and, if the event was applied, the inbox makes a later reprocess of the original a duplicate. The opposite order could lose the message.

### What the copy carries

A new `ServiceBusMessage` with the same body, `MessageId` and `ContentType`, the same shape `ServiceBusEventPublisher` sends. The `ServiceBusMessage(ServiceBusReceivedMessage)` constructor would also copy application properties, which nothing sets today, so the explicit copy shows exactly what the worker receives. The copy starts with `DeliveryCount` 1 and no dead-letter reason (checked by the integration test).

### Cost and limits

Every reprocess locks the messages ahead of the match and then abandons them. The Service Bus docs say an abandon raises `DeliveryCount`; in the emulator the other messages kept 5. Dead-lettered messages have no max delivery count, so that is harmless either way. With the alert firing at one message, the dead-letter should stay small. Both points are in `docs/pending.md` to check against the real namespace.

An unknown sequence number costs one empty receive, 2 s, before the `404`.

## Tests

`tests/Payments.IntegrationTests/Messaging/ServiceBusDeadLetterQueueTests.cs`, on the emulator:

- Three dead-lettered messages: peek lists all three with reason and description; reprocessing the middle one returns its event ID, the dead-letter keeps the other two, and the main queue has the copy with the same ID, body and content type.
- An unknown sequence number returns `null`, and a reprocess right after it finds its message at once, which shows the abandon released the locks.
