# QueueStatsBroadcaster

`src/Payments.Api/Queues/QueueStatsBroadcaster.cs`, with `QueueStatsOptions` and the registration in `src/Payments.Api/Program.cs`.

The `BackgroundService` that sends `QueueStats` to the hub (section 7, task 5.3). It is an adapter, like `PaymentNotificationsListener`: it calls `GetQueueStats` and turns the result into a hub message. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## The loop

A `PeriodicTimer` ticks every `QueueStats:Interval` (5 s in `appsettings.json`, validated as positive at startup). On each tick:

1. With no client connected (`HubConnectionCounter.Count == 0`) it does nothing, so an idle API doesn't peek the queue.
2. Otherwise it runs `GetQueueStats` and, if the outcome is `Read`, sends `QueueStats` to every client. `Unavailable` skips the tick: the use case already logged it.

`PeriodicTimer` doesn't stack ticks: if a read takes longer than the interval, the next tick fires once when it ends, not once per missed interval. The interval is a setting because the tests use 50 to 200 ms.

## Why the use case returns `Unavailable` instead of throwing

Since .NET 6, an exception that escapes `ExecuteAsync` stops the whole host (`BackgroundServiceExceptionBehavior.StopHost`). A Service Bus blip during a peek would take down the API, webhook included. The adapter has no `try`/`catch` (`CLAUDE.md`), so the use case catches the failure and returns `Unavailable` (see `GetQueueStats.md`). `PaymentsHubTests.Unreadable_queue_sends_no_stats_and_keeps_the_api_up` checks that the loop keeps going. Cancellation on shutdown still propagates, which the host expects.

## Time provider

The timer takes `TimeProvider` (registered as `TimeProvider.System`), as the rest of the code does for time. No test fakes it today: the API tests use a short real interval.
