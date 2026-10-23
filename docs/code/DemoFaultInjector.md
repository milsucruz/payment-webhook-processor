# DemoFaultInjector

`src/Payments.Infrastructure/Faults/DemoFaultInjector.cs`, with `NoFaultInjector.cs` next to it and the port `IFaultInjector` in `src/Payments.Application/Processing/IFaultInjector.cs`.

Fault injection for demos (architecture section 6, task 3.3): a test payment with the `demo_fault` metadata makes the worker fail on purpose, so retry, dead-letter and scaling can be shown without relying on luck. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## `InjectAsync`

```csharp
Task InjectAsync(PaymentEvent paymentEvent, int attempt, CancellationToken cancellationToken)
```

`ProcessStripeEvent` calls it at the start of every attempt, before reading the clock and opening the transaction (see `docs/code/ProcessStripeEvent.md`). `attempt` counts from 1 within one delivery, and every call of `ProcessOnceAsync` counts, whether it follows a transient error or a concurrency conflict.

| `demo_fault` | What happens | Effect on the message |
| --- | --- | --- |
| absent | Nothing | Normal processing |
| `transient` | Attempts 1 and 2 throw `TimeoutException`; attempt 3 and later pass | Retried after 1 s and 5 s (200 ms and 1 s until task 4.10), applied on the third attempt, completed on the first delivery |
| `poison` | Every attempt throws `InvalidOperationException` | Leaves the use case, the SDK abandons it, and after 5 deliveries Service Bus dead-letters it with `MaxDeliveryCountExceeded` |
| `slow` | Attempt 1 waits 10 s with the injected `TimeProvider`, then goes on; later attempts don't wait | Applied about 10 s late, with the real commit time recorded; with 4 concurrent calls per replica, a batch of them keeps the queue long enough for scaling |
| anything else | Nothing, plus a warning on attempt 1 naming the value and the event | Normal processing |

Each injected fault is logged as a warning with the fault, attempt, event and payment, so a demo shows in the logs that the failure was injected and not real.

## Choices behind it

- **Attempts within one delivery, not deliveries.** Section 6 says `transient` "fails the first two attempts". Counting the attempts of the 1 + 3 retry loop (task 3.1) makes the demo show that loop: two waits and a commit on the third attempt, all in one delivery, which is the task's done criterion. Counting deliveries would need `DeliveryCount` from the worker, would never exercise the in-process retry, and would cost two abandons. The attempt number is passed in by the use case, so the injector keeps no state and gives the same result after a restart.
- **`TimeoutException` for `transient`.** `SqlTransientErrorDetector` already counts a `TimeoutException` as transient (see `docs/code/SqlTransientErrorDetector.md`), so the injected fault goes through the real classification and the real retry, and the detector knows nothing about demos. A custom exception type would have needed a case in the detector just for the demo.
- **`InvalidOperationException` for `poison`, not a result.** A poison message is one that fails on every delivery with an error nobody classified: the glossary's permanent errors (unreadable event, unsupported type, impossible conflict) go to the dead-letter on the first delivery, everything else takes the 5 deliveries. Throwing an unclassified exception reproduces that second path, which is also the one an unexpected bug in production would take.
- **A port with two implementations, chosen in `Program.cs`.** Section 6 says injection "only exists" when `FaultInjection:Enabled` is on. With the setting off, `Program.cs` registers `NoFaultInjector`, which returns at once, so `DemoFaultInjector` is not in the container at all and no metadata can trigger it. One class that checked the setting on every call would keep the demo code on the production path, guarded only by an `if`.
- **In Infrastructure.** The rule in `CLAUDE.md` puts every implementation of an Application port in Infrastructure. The use case only knows that something may run inside each attempt.
- **Before the clock and the transaction.** The first version ran the injector right after `BeginTransactionAsync`, so a fault would go through the same rollback path as a real database error. The code review of task 3.3 found two problems with that, both from `slow`. `processedAt` was read before the 10 s wait, so the inbox, the payment and the history recorded a time 10 s earlier than the commit, which hid the slowdown the demo is meant to show. And each slow message held a pooled connection with an open transaction for 10 s; a `flood` of them (task 4.7) with 4 concurrent calls per replica, and more replicas as it scales, would press the connection and session limits of the free database and cause real transient errors. Running first costs nothing for `transient` and `poison`: nothing is read or written yet, so there is nothing to roll back, the exception still goes through the same retry or reaches the worker, and a `poison` event never reaches the inbox. `Time_spent_in_the_fault_injector_is_not_hidden_in_the_recorded_times` guards the order.
- **`slow` waits once per delivery.** Section 6 says `slow` "delays 10 s". Waiting on every attempt would turn a slow event that loses a concurrency race, or meets a transient error, into 20 s or more, on top of the real retry waits. The unknown-value warning is limited to attempt 1 for the same reason: one warning per delivery, not one per retry.
- **Faults also hit duplicates.** The injector runs before the inbox insert, so a redelivery or a `replay` of an event already processed still gets its fault (a `slow` duplicate waits 10 s before it ends as `Duplicate`). Checking the inbox first would need a read the use case doesn't do today, only for the demo. Accepted: the demos use fresh payments.
- **Unknown values are ignored.** The metadata is free text set by whoever creates the payment. A typo must not turn a real event into an error, so it only logs a warning. The values are compared exactly (`Poison` is unknown), the same as Stripe metadata keys and values.
- **Read by the parser, interpreted here.** `StripeEventParser` copies `data.object.metadata.demo_fault` into `PaymentEvent.DemoFault` as a raw string, or `null` (see `docs/code/StripeEventParser.md`). The parser doesn't know the three values, so adding one only changes this class.

## Configuration

| Where | Value |
| --- | --- |
| `src/Payments.Worker/appsettings.json` | `FaultInjection:Enabled` = `false` |
| `docker-compose.yml`, `worker` service | `FaultInjection__Enabled: "true"` |

The default is off, so a worker run outside compose, and the cloud until a task turns it on, never injects. The API doesn't read the setting: it never processes events.

## How it was verified (task 3.3)

Unit tests: `tests/Payments.Infrastructure.Tests/Faults/DemoFaultInjectorTests.cs` checks each value against the real `SqlTransientErrorDetector` (transient on attempts 1 and 2, not on 3; poison never transient; slow not done after 9 s, done after 10 s with `FakeTimeProvider`, and no wait on attempts 2 and 4; absent and unknown values do nothing, and an unknown value logs once). `ProcessStripeEventTests` checks that the use case passes attempts 1, 2, 3 to the injector and opens a transaction only for the attempt that passes, that time spent in the injector shows in `ReceivedAt`, `UpdatedAt` and `OccurredAt`, that a concurrency retry is a new attempt, and that an exception that is not transient opens no transaction and writes nothing. Changing `attempt <= 2` to `attempt < 2` fails the transient test, and moving the injector back after `GetUtcNow` fails the time test.

End to end, with `docker compose up -d`, `stripe listen` and `stripe trigger payment_intent.succeeded ... --add "payment_intent:metadata[demo_fault]=transient"`: both events of the payment logged the injected fault on attempts 1 and 2, a retry after 200 ms and 1 s (the delays until task 4.10), and then `Applied` and `IgnoredOutOfOrder`. The payment ended paid, with two inbox rows. With `=poison`: each event logged the fault on attempt 1 five times, a throwaway peek found both in the dead-letter with `DeliveryCount=5` and `MaxDeliveryCountExceeded`, and the database had no row for that payment. `slow` was checked only by the unit test.

The integration test of the poison message with Testcontainers is task 3.5.

## Refunds of a faulty payment (M3 review)

For `charge.refunded`, `data.object` is the charge, so the parser reads the charge's metadata. Checked in the M3 review with `stripe trigger payment_intent.succeeded --remove payment_intent:payment_method_types --add "payment_intent:automatic_payment_methods[enabled]=true" --add "payment_intent:automatic_payment_methods[allow_redirects]=never" --add "payment_intent:metadata[demo_fault]=metadata_check"` (the flags the account needs, see `CLAUDE.md`) and `stripe charges list --limit 1`: the charge the PaymentIntent created carries `"demo_fault": "metadata_check"`. Stripe copies the PaymentIntent's metadata to the charge when it creates the charge. A change to the PaymentIntent's metadata after the charge exists is not copied.

So a refund of a `poison` payment is also poison. That conclusion rests on three separate checks, not on one run: the `stripe charges list` above (the charge inherits the metadata), `StripeEventParserTests` (a `charge.refunded` with `demo_fault` in the charge's metadata gives that value) and `DemoFaultInjectorTests` (`poison` fails every attempt). A `charge.refunded` of a `poison` payment was never sent through the worker end to end; the second M3 review decided the three checks are enough.
