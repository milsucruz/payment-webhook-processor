# PayMode

`tools/Simulator/PayMode.cs`, with `TestCard.cs` and `TestModeStripeClient.cs`

The simulator's `pay` mode (architecture section 11, "Simulator"): creates payments in Stripe test mode, one per test card, so the webhooks that Stripe sends cover approval, decline and authentication. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## The cards

| Name | Payment method | Expected status | Events |
| --- | --- | --- | --- |
| `approve` | `pm_card_visa` | `succeeded` | `created`, `succeeded` |
| `decline` | `pm_card_chargeDeclined` | `requires_payment_method` | `created`, `payment_failed` |
| `authenticate` | `pm_card_authenticationRequired` | `requires_action` | `created`, `requires_action` |

The `pm_card_...` values are Stripe's test payment methods. They avoid sending raw card numbers, which the API refuses unless the account enables it.

## The request

Each payment is 2000 cents in `brl` and is created with `confirm=true`, so one call produces the whole lifecycle up to the card's outcome. Amounts are `long` cents, as everywhere in the project.

`automatic_payment_methods[enabled]=true` and `allow_redirects=never` replace `payment_method_types`, for the same reason the `stripe trigger` command in `CLAUDE.md` removes it: the account's API version rejects that parameter. Stripe.net 53 no longer has the property at all. Without `allow_redirects=never`, confirming without a `return_url` fails.

A declined card makes Stripe answer `402`, which Stripe.net raises as `StripeException`. The error carries the payment intent, so the mode prints it and goes on to the next card instead of stopping. Any other `StripeException` (bad key, network) is not caught.

## The key: `TestModeStripeClient`

`Stripe:SecretKey`, from `dotnet user-secrets` in `tools/Simulator` or the `Stripe__SecretKey` environment variable. It never goes in a file in the repository.

`TestModeStripeClient.Create` refuses any key that doesn't start with `sk_test_` or `rk_test_`. The simulator creates and refunds payments, and the check makes it impossible to point it at live mode by mistake. A restricted key needs write access to PaymentIntents and Refunds.

## Tests

`tests/Simulator.Tests/StripeOptionsTests.cs` checks the request options (cards, amount, currency, confirm, automatic payment methods) and the key check. The calls to Stripe are not tested automatically: they need the network and a key.

## Not verified yet

Task 2.8 had no test-mode secret key available, so `pay` was not run against Stripe (see `docs/pending.md`).
