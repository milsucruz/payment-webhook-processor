# ReprocessDeadLetter

`src/Payments.Application/DeadLetters/ReprocessDeadLetter.cs`, next to `ListDeadLetters.cs` in the same folder.

Use case behind the dashboard's reprocess button (architecture section 6, task 3.4). `DeadLettersController` calls it (see `docs/code/DeadLettersController.md`). This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## What it does

It asks the `IDeadLetterQueue` port to reprocess the message with the given sequence number and returns `ReprocessDeadLetterResult`:

| Port returns | Result | Log |
| --- | --- | --- |
| The event ID | `Reprocessed` | Information, with event ID and sequence number |
| `null` | `NotFound` | Warning, with the sequence number |

The receive, send and complete all live in the adapter (`docs/code/ServiceBusDeadLetterQueue.md`), because they must happen while the same receiver holds the lock. Splitting them across port calls would mean passing a locked Service Bus message through Application.

## Why it doesn't catch exceptions

`ReceiveStripeEvent` catches publish failures because it has something to decide: log a cancelled request as a warning and answer `500` so Stripe resends. Here a Service Bus failure has no expected handling: the operator sees `500` and presses the button again. ASP.NET Core already logs the unhandled exception as an error. If the failure happens after the send and before the complete, the extra copy is a duplicate for the inbox.

## `ListDeadLetters`

The other use case of the folder only calls `PeekAsync` with `MaxMessages` (100) and has no outcome to log, so it has no document of its own. It exists so that the controller depends on use cases and not on a port, like every other adapter (layering rule in `CLAUDE.md`). The limit has no pagination: the alert fires at one message, so a dead-letter with more than 100 means something else is wrong.

## Tests

`tests/Payments.Application.Tests/DeadLetters/`, with `InMemoryDeadLetterQueue`, which the API tests also use: both results with their log level, and the list asking for 100.
