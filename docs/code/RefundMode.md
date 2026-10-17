# RefundMode

`tools/Simulator/RefundMode.cs`

The simulator's `refund` mode (architecture section 11, "Simulator"): `refund <pi_...> [amount-in-cents]` refunds a payment, fully without an amount, partially with one. Stripe then sends `charge.refunded`, which the parser maps to refunded or partially refunded (`docs/code/StripeEventParser.md`). This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Choices

- **The payment is named explicitly.** Picking payments from the database or from Stripe would need rules about which ones are refundable. The ID is printed by `pay` and is in the `Payments` table.
- **A full refund leaves `amount` out.** Stripe then refunds what is left, so a full refund after a partial one works without computing the remainder.
- **The amount is `long` cents**, parsed from the argument and refused unless positive. Stripe refuses an amount above what is left; the error is not caught, so it is shown as is.
- Two partial refunds in a row exercise the rule that a second partial refund raises the refunded amount and keeps the level (section 6).

The key is the same as `pay` (`TestModeStripeClient`, see `docs/code/PayMode.md`).

## Tests

`tests/Simulator.Tests/StripeOptionsTests.cs` checks that no amount means a full refund and that an amount is passed through.

## Not verified yet

Task 2.8 had no test-mode secret key, so this mode was not run against Stripe. The partial and full refunds used in the replay check were made with `stripe refunds create` (see `docs/pending.md`).
