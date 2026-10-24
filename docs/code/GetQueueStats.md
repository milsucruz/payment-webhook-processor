# GetQueueStats

`src/Payments.Application/Queues/GetQueueStats.cs`, with `IQueueStatsReader`, `QueueStats`, `GetQueueStatsOutcome` and `GetQueueStatsResult` in the same folder.

The use case behind the hub's `QueueStats` message (section 7, task 5.3): how many messages are waiting in `payment-events` and how many are in its dead-letter. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Outcomes

| Outcome | When | Log |
| --- | --- | --- |
| `Read` | The reader returned the counts | None: it runs every 5 s while the dashboard is open |
| `Unavailable` | The reader threw anything but an `OperationCanceledException` while the caller's token is cancelled | Warning, with the exception |

The catch is broad on purpose. Application doesn't know Azure's exception types (the port hides them), and any failure to count has the same consequence, a skipped tick. A bug in the reader shows up the same way, as a Warning with its stack every 5 s, which is visible enough. The filter lets only the caller's own cancellation through, so a shutdown ends the loop. The first version let through any exception once the token was cancelled, so a `ServiceBusException` that arrived during shutdown escaped the broadcaster and the host logged "BackgroundService failed" on a normal stop (code review, `Failure_while_the_caller_is_cancelling_is_still_unavailable`). Why the caller needs an outcome instead of an exception is in `QueueStatsBroadcaster.md`.

During a Service Bus outage with the dashboard open, this logs a Warning with a stack every 5 s, the same kind of noise as the readiness check. `docs/pending.md` lists it with the other log volume items for task 5.6.

## Tests

`tests/Payments.Application.Tests/Queues/GetQueueStatsTests.cs`, with `FakeQueueStatsReader` (also linked into the API tests): the stats come back without a log, a failure returns `Unavailable` with one Warning (also when it arrives while the caller is cancelling), and a cancelled caller gets the cancellation.
