# PaymentsMetrics

`src/Payments.Application/Telemetry/PaymentsMetrics.cs`

The five custom metrics of section 9 (task 5.7), as one class with one method per fact. `ReceiveStripeEvent` (API), `ProcessStripeEvent` and `PaymentEventsWorker` (worker) call it; `PaymentsTelemetry` exports the meter (see `docs/code/PaymentsTelemetry.md`).

## Instruments

| Instrument | Recorded by | When | Tag |
| --- | --- | --- | --- |
| `events.received` | `ReceiveStripeEvent` | After the publish to `payment-events` succeeds | `payments.event_type` (the event's `type`, or `unknown`) |
| `events.duplicate` | `ProcessStripeEvent` | The delivery ends as `Duplicate` | none |
| `events.out_of_order` | `ProcessStripeEvent` | The delivery ends as `IgnoredOutOfOrder` | none |
| `events.dead_lettered` | `PaymentEventsWorker` | After `DeadLetterMessageAsync` returns | `payments.dead_letter_reason` (`InvalidEvent`, `ImpossibleConflict`) |
| `event.processing.duration` | `ProcessStripeEvent` | After the commit of `Applied`, `IgnoredOutOfOrder` or `Redundant` | `payments.outcome` |

The names are the ones in section 9, and the tags follow the `payments.` prefix of the transaction span's tags, so a dimension in `customMetrics` and a tag on a span read the same.

## Where each one is counted

Each metric is counted where the fact it describes becomes true:

- **Received after the publish.** An event is "in" when it is in the queue: a rejected signature is not an event, and a failed publish answers `500` and Stripe resends, so counting before the publish would count it twice. A resend that Stripe makes after a `200` is counted again, because it did come in; the worker's `events.duplicate` shows how many of those idempotency absorbed. The type comes from the same `JsonDocument` that already reads the event ID, after the signature check, so it is Stripe's own value and the tag has as many values as the event types Stripe sends.
- **Duplicate and out of order in the use case.** `ProcessStripeEvent` already logs these outcomes; the counter sits next to the log, on the final outcome of the delivery, after the concurrency retries.
- **Dead-lettered in the worker.** The use case returns `InvalidEvent` or `Conflict`, but only the worker moves the message, and only after `DeadLetterMessageAsync` returns is it in the dead-letter (if the lock is lost first, the message comes back and is counted on the delivery that does move it). The reason codes are also the worker's constants.
- **No `MaxDeliveryCountExceeded`.** Service Bus moves a message abandoned 5 times by itself; the worker only sees the abandon, and guessing that the fifth abandon is the last would repeat the queue's `maxDeliveryCount` in code and be wrong when a lock expires. The Service Bus `DeadletteredMessages` metric counts every path, and the dead-letter alert uses it.

## Duration from the enqueue time

Section 9 asks "how long from receipt to commit". `PaymentEventsWorker` passes the message's `EnqueuedTime` to `ExecuteAsync` as `receivedAt`, which the broker sets milliseconds after the API accepted the webhook, and the use case records `now - receivedAt` on the `TimeProvider` after the commit. Measuring only inside `ExecuteAsync` would hide the time the event waited in the queue, the transient retry delays and earlier deliveries, which is what grows when the database is paused. `Duplicate` records nothing: its transaction rolls back, so there is no commit to measure. A reprocessed dead-letter is a new message with a new enqueue time, so its duration starts at the reprocess.

The unit is seconds (`s`), the OpenTelemetry convention for durations. The Azure Monitor exporter sends a histogram as sum, count, minimum and maximum per minute, without buckets.

## `IMeterFactory`, not a static meter

The meter comes from `IMeterFactory`, registered by the host (`AddMetrics`, which `Host.CreateApplicationBuilder` and `WebApplication.CreateBuilder` call; the worker's `AddPaymentEventsWorker` calls it too, for the tests that build a host without the defaults). A static `Meter`, like the static `ActivitySource` in `ProcessStripeEvent`, would be shared by every test in the process, and a test running in parallel would see the other tests' measurements. With the factory each test creates its own meter (`tests/Payments.Application.Tests/Telemetry/TestMeterFactory.cs`) and `MetricCollector<T>` listens only to it. The class is not a port: like `ILogger` and `ActivitySource`, `System.Diagnostics.Metrics` is part of .NET and needs no implementation in Infrastructure.

## Tests

- `ReceiveStripeEventTests`: a published event is counted once with its type; an event without a type counts as `unknown`; an invalid signature, a body without an event ID and a failed publish count nothing.
- `ProcessStripeEventTests`: an applied event records 2 s (the test's `receivedAt` is 2 s before the clock) with its outcome and no counter; two transient retries make it 8 s; a duplicate counts once and records no duration; an out-of-order event counts once and records its duration; an impossible conflict and an invalid event record nothing.
- `PaymentEventsWorkerDeadLetterTests` (integration): an unreadable event on the emulator is counted once as `InvalidEvent`, with the test's `IMeterFactory` replacing the host's.
