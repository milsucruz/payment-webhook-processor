# PaymentTransitions

`src/Payments.Domain/PaymentTransitions.cs`, with `PaymentStatus.cs` and `TransitionOutcome.cs` next to it.

The state machine of section 6 of the architecture: given the current state of a payment and the state an event requests, it decides what processing that event does. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## `Decide`

```csharp
TransitionOutcome Decide(PaymentStatus current, long currentAmountRefunded, PaymentStatus requested, long requestedAmountRefunded)
```

| Outcome | Meaning | Who acts on it |
| --- | --- | --- |
| `Applied` | The payment moves to the requested state (or its refunded amount grows) | Use case (task 2.4) |
| `IgnoredOutOfOrder` | The event would lower the level; recorded in the history, not applied | Use case |
| `Redundant` | The payment is already there, for example two distinct Stripe events for the same fact | Use case |
| `Conflict` | An impossible conflict; the message goes to the dead-letter with its own reason | Use case and worker (tasks 2.4 and 3.x) |

It is a pure function: no I/O, no clock, no entity. The use case loads the payment and calls it.

### Why the input is the requested state, not the Stripe event type

`charge.refunded` leads to two different states, partially refunded or refunded, depending on the amounts in the snapshot. If the domain received Stripe event types, it would have to read amounts from the charge to know which. Translating the event type into a requested state belongs to the parser (task 2.2), so the domain only knows states. It also matches `PaymentHistory`, which stores `FromStatus` and `ToStatus`.

### Why the refunded amounts are parameters

A second partial refund keeps the state but changes the amount, and the amount never decreases. The partially refunded to partially refunded cell can't be decided by state alone:

| Requested amount vs. current | Outcome |
| --- | --- |
| Larger | `Applied` |
| Equal | `Redundant` |
| Smaller | `IgnoredOutOfOrder`: an older refund arriving late |

Leaving this rule to the use case would split the out-of-order rules across two layers, and the property test (task 2.7) needs all of them in one place. The amounts are plain `long` cents instead of a `Payment` entity, which arrives with the persistence work (tasks 2.3 and 2.4).

## Rule order

1. **Impossible conflict:** "paid" or a refund for a canceled payment, or "canceled" for a payment that was paid (paid, partially refunded or refunded).
2. **Partial refund on a partial refund:** compare the amounts, as above.
3. **Same state:** redundant.
4. **Switch between requires action and processing:** applied, the only change allowed within a level.
5. **Levels:** applied if the requested level is higher, otherwise ignored as out of order.

The conflict comes first because some conflicts would otherwise be applied by the levels (canceled, level 3, to refunded, level 5).

### Why there is no final-state rule

Section 6 says canceled and refunded accept no transition. The first version had an explicit rule for it, between steps 3 and 4. Turning that rule off left all 64 table tests green: it was never reached with a different result. Refunded is the highest level, so everything else is lower. For canceled, the only events that would not lower its level (paid and the two refunds) are already impossible conflicts. Final states follow from rules 1 and 5, so the rule was removed instead of keeping a branch no test can observe. If a state above refunded is ever added, the table test fails for the new column, because it lists every pair of states.

### Why levels are private

Two states share level 1 (requires action, processing) and two share level 3 (paid, canceled), so the enum value is not the level. Nothing outside this class needs the level, and the tests check outcomes, not levels.

## Enum values

Both enums are `byte`, because they become `tinyint` columns (`Payments.Status`, `InboxEvents.Outcome`). Values are explicit and start at 1: once rows are stored they can't be renumbered, and `default` (0) is never a valid value, so a field nobody set doesn't pass for `Created`.

## Tests

`tests/Payments.Domain.Tests/PaymentTransitionsTests.cs`:

- The expected table is written out literally, with 64 rows for every pair of current and requested state. It is not computed from the levels, or the test would repeat the logic it checks. The rows pass equal refunded amounts, so partially refunded to partially refunded is `Redundant`.
- Two facts cover the larger and the smaller partial refund.
- One fact checks that the table has every pair exactly once, so a new state can't be added without a decision for each of its cells.

Turning off the switch between requires action and processing fails one row, which is how the table was checked to detect a broken rule.
