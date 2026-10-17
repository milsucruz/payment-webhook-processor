# ProcessStripeEvent

`src/Payments.Application/Processing/ProcessStripeEvent.cs`, with `IPaymentStore.cs`, `ProcessStripeEventResult.cs`, `ProcessStripeEventOutcome.cs`, `InboxEvent.cs` and `PaymentHistoryEntry.cs` next to it, and `Payment.Apply` in `src/Payments.Domain/Payment.cs`.

Processing use case: turns a Stripe event taken from the queue into a change to the payment, writing inbox, state and history in one transaction (architecture sections 4 to 6). The worker calls it in task 2.5. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

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

The worker (task 2.5) maps the result: `InvalidEvent` and `Conflict` are permanent errors that go to the dead-letter with `Reason`; the other four complete the message.

## Why the inbox comes after the read

Section 4 used to say "insert into the inbox, then load the payment". That order is impossible: `InboxEvents.Outcome` is `NOT NULL`, and `InboxEvent` is insert-only (`init`), so the row can only be built once the outcome is known, and the outcome needs the payment. The transaction makes the order invisible from outside: either the three writes commit together or none does. Section 4 now describes this order.

Two consequences:

- **An impossible conflict returns before the inbox.** That is safe for duplicates: an event that reached the inbox was applied, ignored or redundant, never a conflict, and a conflict can't appear later for the same event (canceled and the "paid" family can't both be applied to one payment). So no duplicate is hidden behind a `Conflict`.
- **A duplicate is decided against the current state, and the decision is thrown away.** The inbox insert fails, the transaction rolls back, and nothing the decision changed in memory reaches the database.

`PaymentStore.md` explains how the separate inbox `SaveChanges` keeps a duplicate apart from a concurrency conflict without looking at the violated table, which is task 2.6.

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
| Retry after a `RowVersion` failure or a `Payments` primary key violation | Task 2.6. Both surface as exceptions from the store and are not caught |
| Retry of transient database errors | Task 3.1. Exceptions propagate to the worker |
| `TraceId` | M5 (5.6). Always `null` until there is an `Activity` |
| Duplicate metric (section 6) | M5 (5.7) |
| Registration in the worker's container | Task 2.5 |

## Tests

`tests/Payments.Application.Tests/Processing/ProcessStripeEventTests.cs` runs the real use case against `InMemoryPaymentStore`, a test double whose pending writes become visible only on `CommitAsync` and are discarded when the transaction is disposed. It mimics the inbox primary key by rejecting a repeated event ID. Events come from the real fixtures in `tests/fixtures`; expected values are read through `StripeEventParser` instead of copied.

The task's done criterion maps to `Duplicate_event_writes_nothing_and_returns_duplicate`, `Event_for_an_unknown_payment_creates_it_in_the_requested_state` and `Partial_refund_of_a_paid_payment_records_the_refunded_amount`. A mutation that ignores the inbox result fails the duplicate test. `tests/Payments.Domain.Tests/PaymentTests.cs` covers `Apply`.
