# ServiceBusDeadLetterQueue

`src/Payments.Infrastructure/Messaging/ServiceBusDeadLetterQueue.cs`

Implements the `IDeadLetterQueue` port (Application) over the dead-letter subqueue of `payment-events`: peek for the list, and reprocess one message by sequence number. The API registers it as a singleton with the API's `ServiceBusClient`. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Peek

`PeekAsync` opens a receiver on `SubQueue.DeadLetter` and peeks until it has the requested count or a peek comes back empty. A single peek can return fewer messages than asked even when more exist, hence the loop; each peek on the same receiver continues after the last sequence number it returned. Peeking takes no lock and doesn't remove anything, which is what "without removing the messages" in the task means.

## Reprocess: why it scans

Service Bus can't receive a specific message by sequence number. `ReceiveDeferredMessageAsync` takes sequence numbers, but only for deferred messages, and a dead-lettered message is not deferred. This was listed as a risk in section 14 of the architecture, and task 3.4 uses its plan B:

1. Receive from the dead-letter in batches of 50 (peek-lock, prefetch 0), waiting at most 1 s per batch.
2. Keep every message that is not the one asked for locked in a list, so the next batch returns new messages instead of the same ones.
3. Stop at the match, at an empty batch, or after 500 messages (`MaxScannedMessages`), which bounds the time a request can hold locks.
4. On a match: send a copy to `payment-events`, then complete the original.
5. Always (`finally`): abandon every message held in step 2, and the match too if it wasn't completed (the send failed or the request was cancelled first), so they are back in the dead-letter.

The abandons in step 5 run in parallel and each one swallows `ServiceBusException`. A failed abandon only means that message stays locked until its lock expires (60 s), and it must neither stop the other abandons nor turn a reprocess that already sent and completed into a `500`. The code review of task 3.4 found both problems in the first version, which abandoned one by one with no guard, and left the match locked for 60 s when the send failed.

Once the send has succeeded, the complete runs with `CancellationToken.None`. If the operator's request were cancelled between the two, the copy would be in the queue and the original back in the dead-letter, and the next click would send a second copy. Finishing the complete avoids that, and it takes milliseconds.

The lock duration is 60 s, and 500 messages in batches of 50 take far less, so no held lock expires during a scan.

### Why publish first, then complete

Section 6 sets the order: publish a copy, then complete the original. If the process dies between the two, the copy is already in the main queue and the original is still in the dead-letter, so a second reprocess sends the event twice. The inbox makes that a `Duplicate`, so nothing is applied twice. The other order could lose the event: completed in the dead-letter, never published.

### The copy

`new ServiceBusMessage(received)` copies the body, `MessageId`, `ContentType` and application properties, and drops what the broker set: sequence number, delivery count, lock, enqueued time and the dead-letter reason and description. The integration test checks that the copy arrives with the same `MessageId`, body and content type, a delivery count of 1 and no `DeadLetterReason`. The copy starts a new life in the main queue, with five fresh deliveries.

The class also removes the `AbandonReason` property from the copy. Otherwise a reason written during an outage last week would come back with the copy, and if the message ended in the dead-letter again as a poison message, the screen would show the stale outage reason. The mutation check in task 3.4 (deleting that line) fails the test.

The copy goes through its own `ServiceBusSender`, not through `IEventPublisher`. The publisher builds a message from an ID and a JSON string; the copy constructor keeps whatever the original carried, without the adapter having to know which properties matter.

### Known side effects

- **Delivery count in the dead-letter.** Every message held and abandoned during a scan gets one more delivery. The dead-letter has no maximum delivery count, so nothing moves, but the count the list shows grows with each reprocess of another message. It is not used for any decision.
- **Two reprocesses at the same time** may hide each other's messages: a message locked by one scan is invisible to the other, which can answer `404` for a message that is there. With a single operator this is accepted; the second click works once the first request finishes.
- **More than 500 messages** before the one asked for: `404`. The alert fires at the first dead-lettered message, so the dead-letter is expected to stay small.

## Tests

`tests/Payments.IntegrationTests/Messaging/ServiceBusDeadLetterQueueTests` uses the emulator in Testcontainers (same image and `config.json` as compose):

- Three messages dead-lettered with `InvalidEvent`, the second one first abandoned with an `AbandonReason`. Peek lists the three in order with reason, description and abandon reason. Reprocessing the second returns its `evt_` ID, the dead-letter keeps the other two, and the main queue has the copy with the same ID, body and content type, delivery count 1, no dead-letter reason and no `AbandonReason`.
- An unknown sequence number returns `null`, the two messages are still in the dead-letter after the scan abandoned them, and nothing reaches the main queue.
