# ProcessStripeEvent

`src/Payments.Application/Processing/ProcessStripeEvent.cs`, with `IPaymentStore.cs`, `ProcessStripeEventResult.cs`, `ProcessStripeEventOutcome.cs`, `InboxEvent.cs` and `PaymentHistoryEntry.cs` next to it, and `Payment.Apply` in `src/Payments.Domain/Payment.cs`.

Processing use case: turns a Stripe event taken from the queue into a change to the payment, writing inbox, state and history in one transaction (architecture sections 4 to 6). `PaymentEventsWorker` calls it (see `docs/code/PaymentEventsWorker.md`). This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## `ExecuteAsync`

```csharp
Task<ProcessStripeEventResult> ExecuteAsync(string payload, CancellationToken cancellationToken)
```

The payload is the message body, the original JSON that the API published. The use case parses it itself because the inbox stores it as is (`Payload`), and because an unreadable body must turn into a result, not an exception (see "Invalid event").

| Step | Result | Writes | Log |
| --- | --- | --- | --- |
| 1. Parse with `StripeEventParser` | `InvalidEvent`, reason = exception message | Nothing, no transaction | Warning |
| 2. Open the transaction and read the payment | | | |
| 3. Decide: `Payment.Apply`, or "applied" for an unknown payment | `Conflict`, reason names event, payment and state | Nothing (rollback) | Warning |
| 4. Insert the inbox row with the outcome | `Duplicate` if the event ID is already there | Nothing (rollback) | Information |
| 5. Add the new payment, if unknown, and the history row; commit | `Applied`, `IgnoredOutOfOrder` or `Redundant` | Inbox, payment, history | Information |

Steps 2 to 5 are one attempt (`ProcessOnceAsync`). A concurrency conflict in step 4 or 5 rolls the attempt back and starts again at step 2; see "Concurrency conflicts" below.

The worker maps the result: `Applied`, `IgnoredOutOfOrder`, `Redundant` and `Duplicate` complete the message. `InvalidEvent` and `Conflict` are permanent errors that go to the dead-letter with `Reason` (task 3.2); until then the worker abandons them. `ConcurrencyConflict` is abandoned for a later delivery.

## Concurrency conflicts

Two replicas (or two of the 4 concurrent calls of one worker) can process events for the same payment at the same time. Both read the same state, and the second write fails: on `RowVersion` when the payment exists, on the `Payments` primary key when both try to create it (section 6). The store reports both as `PaymentStoreWriteResult.ConcurrencyConflict` (see `docs/code/PaymentStore.md`).

`ExecuteAsync` parses once and then calls `ProcessOnceAsync` up to `MaxAttempts` (3) times:

| Attempt result | What happens | Log |
| --- | --- | --- |
| Anything but `ConcurrencyConflict` | Returned as is | As in the table above |
| `ConcurrencyConflict`, attempts left | The `await using` disposes the transaction (rollback), and the next attempt opens a new one and reads again | Information, with payment, event and attempt number |
| `ConcurrencyConflict` on the last attempt | Returns `ConcurrencyConflict` with a reason naming event and payment | Warning |

Choices behind it:

- **In the use case, not the worker.** Section 6 says "the worker" retries, meaning the process. The worker class is an adapter with no `try`/`catch` (layering rule in `CLAUDE.md`), and a retry from the read means running the decision again, which is the use case's job.
- **No wait between attempts.** The conflict only happens after the other write committed (`PaymentStore.md`), so the next read already sees the new state. The 1 + 3 retries with 200 ms, 1 s and 5 s in section 6 are for transient errors (task 3.1), a different case.
- **3 attempts.** One retry is enough for two events racing. The third covers a third event committing between the retry's read and write. Losing three in a row needs a burst on one payment; then the message goes back to the queue instead of looping.
- **A result, not an exception.** The store already reported a duplicate as a value. A concurrency conflict is just as expected, so it gets a value in the same enum (rule in `CLAUDE.md`).
- **`processedAt` per attempt.** Each attempt reads the clock again, so `UpdatedAt`, `ReceivedAt` and `OccurredAt` are the time of the attempt that committed.
- **A separate outcome name.** `Conflict` already means impossible conflict (dead-letter). `ConcurrencyConflict` never goes to the dead-letter on its own. Both terms are in the glossary (section 1).

## Why the inbox comes after the read

Section 4 used to say "insert into the inbox, then load the payment". That order is impossible: `InboxEvents.Outcome` is `NOT NULL`, and `InboxEvent` is insert-only (`init`), so the row can only be built once the outcome is known, and the outcome needs the payment. The transaction makes the order invisible from outside: either the three writes commit together or none does. Section 4 now describes this order.

Two consequences:

- **An impossible conflict returns before the inbox.** That is safe for duplicates: an event that reached the inbox was applied, ignored or redundant, never a conflict, and a conflict can't appear later for the same event (canceled and the "paid" family can't both be applied to one payment). So no duplicate is hidden behind a `Conflict`.
- **A duplicate is decided against the current state, and the decision is thrown away.** The inbox insert fails, the transaction rolls back, and nothing the decision changed in memory reaches the database.

`PaymentStore.md` explains how the store tells a duplicate apart from a concurrency conflict by the name of the violated key.

## `Payment.Apply`

```csharp
TransitionOutcome Apply(PaymentStatus requested, long amountRefunded, string eventId, DateTimeOffset occurredAt)
```

The decision stays in `PaymentTransitions.Decide`; `Apply` only acts on it. Only an applied event changes the payment, and it changes `Status`, `AmountRefunded`, `LastEventId` and `UpdatedAt`. `Amount`, `Currency` and `CreatedAt` are set once, by the constructor.

- `LastEventId` is "the last event that changed the state" (section 5), so an ignored or redundant event leaves it alone.
- `AmountRefunded` is assigned, not `Math.Max`-ed. `Decide` already guarantees it never goes down: an applied event either raises the level (and refunds are the top two levels) or is a larger partial refund. `PaymentTests` covers a smaller partial refund being ignored.
- A method named after the domain operation, not `SetStatus`, keeps every caller going through `PaymentTransitions` (entity rule in `CLAUDE.md`).

An unknown payment is created by the constructor already in the requested state, with `CreatedAt = UpdatedAt =` processing time (section 6: a "paid" that arrives before "created" works). Its history row has `FromStatus = null`.

## Result record, not a plain enum

`CLAUDE.md` asks use cases to return an enum. Here the result is `ProcessStripeEventResult(Outcome, Reason)`, because the dead-letter needs a reason (section 6), and for an invalid event the useful reason is the parser's message ("The field 'payment_intent' is missing..."). A fixed text per enum value would hide which field was wrong. `Reason` is set only for `InvalidEvent` and `Conflict`. Decided in the task 2.4 session.

## Duplicate versus redundant

Two different things, both in the glossary (section 1):

| | Same `evt_` ID? | Writes | Result |
| --- | --- | --- | --- |
| Duplicate | Yes, already in the inbox | Nothing | `Duplicate` |
| Redundant | No, another event asking for the current state | Inbox (`Outcome = Redundant`) and history (`Applied = false`, reason `redundant`) | `Redundant` |

Stripe sometimes sends two events for one fact; the inbox can't catch that, the state machine records it as redundant (section 6).

## History reasons

Ignored rows carry the outcome as text: `out of order` or `redundant` (constants on the class, used by the tests). Applied rows have no reason, which is why the column is nullable.

## What is not here yet

| Missing | Where |
| --- | --- |
| Retry of transient database errors | Task 3.1. Exceptions propagate to the worker |
| `TraceId` | M5 (5.6). Always `null` until there is an `Activity` |
| Duplicate metric (section 6) | M5 (5.7) |

## Tests

`tests/Payments.Application.Tests/Processing/ProcessStripeEventTests.cs` runs the real use case against `InMemoryPaymentStore`, a test double whose pending writes become visible only on `CommitAsync` and are discarded when the transaction is disposed. It mimics the inbox primary key by rejecting a repeated event ID. Events come from the real fixtures in `tests/fixtures`; expected values are read through `StripeEventParser` instead of copied.

The task's done criterion maps to `Duplicate_event_writes_nothing_and_returns_duplicate`, `Event_for_an_unknown_payment_creates_it_in_the_requested_state` and `Partial_refund_of_a_paid_payment_records_the_refunded_amount`. A mutation that ignores the inbox result fails the duplicate test. `tests/Payments.Domain.Tests/PaymentTests.cs` covers `Apply`.

For the retry, `InMemoryPaymentStore.ConcurrencyConflictsToReport` makes `CommitAsync` report a conflict that many times. `Concurrency_conflict_is_retried_from_the_read_in_a_new_transaction` checks two transactions and one commit, and `Concurrency_conflict_on_every_attempt_writes_nothing_and_returns_the_reason` checks the limit.

The double can't produce a real race, so task 2.6's done criterion is checked by `tests/Payments.IntegrationTests/Processing/ProcessStripeEventConcurrencyTests.cs`, against SQL Server in Testcontainers:

- Two `ProcessStripeEvent` instances run at the same time, each with its own `DbContext`, on `payment_intent.requires_action` and `payment_intent.succeeded` for the same `pi_`. The events are inline JSON, because each fixture has a different `pi_`.
- `BarrierPaymentStore` wraps `PaymentStore` and holds each run after its first read until both have read, so both decide from the same state and the race happens every time, not by luck. It also counts the `ConcurrencyConflict` results, and the test asserts exactly one, which proves the race really happened.
- Two cases: existing payment in `created` (`RowVersion`) and unknown payment (`PK_Payments`). Both end `paid` with `LastEventId` from `succeeded`, two inbox rows and two history rows. Whichever wins, the other is applied or ignored as out of order on its retry.
- With `MaxAttempts = 1` (no retry), both cases fail (mutation check in the task 2.6 session).

### Property test

`tests/Payments.Application.Tests/Processing/ProcessStripeEventPropertyTests.cs` checks the central claim of section 11: any order and any repetition of a valid event sequence (glossary, section 1) leads to the same resulting state. It runs the use case rather than `Payment.Apply` alone because repetition is handled by the inbox (`Duplicate`) and an unknown payment is created by the use case, from whichever event arrives first. A domain-only test would have to copy that logic.

- **Lifecycles.** Five of them, written by hand with the expected status, refunded amount and `LastEventId`: authenticated, processed and refunded in three steps (7 events); declined twice, paid on retry and partially refunded twice (7); authentication failed then canceled (4); authentication failed with no retry (3); processed asynchronously then paid (3). The events are inline JSON with one `pi_`, because each fixture has a different one. The fixtures stay covered by the parser tests.
- **Permutations are exhaustive.** Every order is delivered, up to 7! = 5040 per lifecycle, and the test asserts it ran n! orders. With so few events, enumerating them all is stronger than sampling, and it needs no library. FsCheck would add shrinking, which a list of 7 events doesn't need.
- **Repetitions are sampled.** 1000 runs per lifecycle with a fixed seed: each event is delivered 1 to 3 times and the whole list is shuffled, so copies also arrive between other events. The seed makes a failure reproducible.
- **What is compared.** Status, amount, refunded amount, currency, `LastEventId`, and the counts of inbox and history rows, which must both equal the number of distinct events. `CreatedAt` and `UpdatedAt` are left out: they are processing times, not facts from the events. A conflict or a rejected event would write no inbox row, so the row count also catches those. A failure prints the delivery order.
- **The limit of the property.** When requires action and processing are the highest events, the last one to arrive wins (the switch rule of section 6), so the order matters. `Requires_action_and_processing_as_the_highest_events_end_in_the_last_to_arrive` pins that behavior, and the glossary excludes such sequences from "valid". The same holds for two distinct events for the same state at the top: `LastEventId` is the first to arrive.

Mutation checks (task 2.7 session), all reverted:

| Mutation | Result |
| --- | --- |
| A smaller partial refund is applied (`!=` instead of `>` in `CompareRefunds`) | Both property tests fail. The first version had no lifecycle that ended with two partial refunds, because the full refund wins whatever the order, and the mutation passed. That is why the second lifecycle has two partial refunds |
| Switch between requires action and processing turned off | The pinning fact fails |
| The use case ignores `DuplicateEvent` from the inbox | The repetition test fails on the history row count |
| `>=` instead of `>` between levels | Passes. It is an equivalent mutant: the only distinct states that share a level are requires action and processing, which have their own rule, and paid and canceled, which are an impossible conflict |
