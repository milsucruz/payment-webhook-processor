# StripeEventParser

`src/Payments.Application/Events/StripeEventParser.cs`, with `PaymentEvent.cs` and `InvalidStripeEventException.cs` next to it.

Turns the raw JSON of a Stripe event into a `PaymentEvent`, the parts of the event that matter to a payment, including the state it requests (architecture section 6). Created in task 2.2; `ProcessStripeEvent` (task 2.4) calls it. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## `Parse`

```csharp
PaymentEvent Parse(string payload)
```

| Event type | `RequestedStatus` | Read from `data.object` |
| --- | --- | --- |
| `payment_intent.created` | `Created` | PaymentIntent `id`, `amount`, `currency` |
| `payment_intent.requires_action` | `RequiresAction` | Same |
| `payment_intent.processing` | `Processing` | Same |
| `payment_intent.payment_failed` | `Failed` | Same |
| `payment_intent.succeeded` | `Paid` | Same |
| `payment_intent.canceled` | `Canceled` | Same |
| `charge.refunded` | `PartiallyRefunded` if `amount_refunded < amount`, `Refunded` otherwise | Charge `payment_intent`, `amount`, `amount_refunded`, `currency` |

`EventId`, `EventType` and `StripeCreatedAt` come from the top level of the event, not from the object. `StripeCreatedAt` is the event's `created`, the column of `InboxEvents` (section 5); the object's `created` is when the PaymentIntent or the charge was created.

It is a static class with a pure function: no state, no I/O, no dependency. It doesn't need a port or a DI registration, the same as `PaymentTransitions`, and it is synchronous, so it takes no `CancellationToken`.

## Why it lives in Application and uses `JsonDocument`

- **Not Stripe.net.** `EventUtility.ParseEvent` maps the JSON to typed models of one API version. The account uses `2026-09-30.endive`, and a mismatch throws unless that check is turned off; typed models also lag new versions. It would have to live in Infrastructure behind a port, for code that only reads six fields. Task 1.2 rejected `ConstructEvent` for the same reason.
- **Not Domain.** The JSON format of Stripe is an adapter concern. The domain knows states, not event types (see `docs/code/PaymentTransitions.md`).
- **Not a port.** Reading JSON with the BCL is not an external dependency, so the rule "every external dependency of a use case is a port" doesn't apply.

## Strict on what it reads, lenient on the rest

It throws `InvalidStripeEventException` when:

- the body is not JSON, or not a JSON object;
- `id`, `type`, `created`, `data` or `data.object` is missing or has the wrong JSON type;
- a field it reads from the object is missing, null, empty or has the wrong JSON type, which includes a charge with `payment_intent: null`;
- the type is not one of the seven subscribed types (section 2).

Every other field is ignored, so new fields that Stripe adds don't break the parser. `api_version` is not checked: Stripe sends events in the version pinned to the account, and a check would turn the next account upgrade into dead-lettered events.

The one optional field is `data.object.metadata.demo_fault` (task 3.3), copied as a raw string into `PaymentEvent.DemoFault`. A missing or null `metadata`, a missing key or a value that is not a string gives `null`, never an exception: metadata is free text set by whoever creates the payment, and a demo flag must not turn a real event into a permanent error. The parser doesn't check the value; `DemoFaultInjector` interprets it (see `docs/code/DemoFaultInjector.md`). For `charge.refunded` it is the charge's own metadata, so a refund carries the fault only if the charge has the key; that was not checked against Stripe, and the demos of task 3.3 use `payment_intent` events. `DemoFault` is the last positional parameter of `PaymentEvent`, with `null` as default, so the existing literal events in the tests stay unchanged.

All of these are permanent errors (section 4): a redelivery carries the same body, so retrying can't help. The exception message is the dead-letter reason; task 2.4 decides how the use case turns it into a result.

### Why an exception and not a result

The done criterion of task 2.2 says an unknown type *raises* a permanent error, and an unreadable event is exceptional: Stripe signs what it sends, and the API already checked that signature. The convention of returning results applies to use case outcomes, and the parser is not a use case. The exception is specific to the parser; the impossible conflict is a different permanent error and already comes back as `TransitionOutcome.Conflict`.

## Amounts

- **`AmountRefunded` is `0` for `payment_intent` events.** A PaymentIntent has no refunded amount field. `PaymentTransitions.Decide` only compares refunded amounts for partially refunded on partially refunded, and `Payments.AmountRefunded` keeps the highest value seen, so `0` never lowers it.
- **`Amount` of a `charge.refunded` is the charge amount.** For the payments this project creates there is one successful charge per PaymentIntent, so it equals the PaymentIntent amount.
- **Partial or full comes from amounts, not from the `refunded` boolean.** Both carry the same information; the comparison is the rule written in the table of section 6, and it uses the same numbers that go to `AmountRefunded`.
- `data.previous_attributes` of a `charge.refunded` also has an `amount_refunded` (the value before the refund). The parser reads only `data.object`.

## Tests

`tests/Payments.Application.Tests/Events/StripeEventParserTests.cs`. The fixtures in `tests/fixtures` are copied to the test output by the test `csproj`.

- One theory row per fixture with the expected `PaymentEvent` written literally.
- A coverage test: the fixture types equal the subscribed types minus `payment_intent.processing`. The test sandbox is a Brazilian account whose payment methods (card, Apple Pay; boleto, Google Pay and Pix unavailable) never go through processing, and `stripe trigger payment_intent.processing` pays with SEPA Debit, which the account can't enable. `processing` is covered by an inline JSON test. The exception is permanent (M2 review): the parser reads only `id`, `amount` and `currency` from it, the same fields as the other `payment_intent.*` events, which have real fixtures, so a second Stripe account in a country with SEPA Debit or ACH would not pay for itself. If a real fixture is ever added, the coverage test fails until the exception is removed.
- Error cases with minimal inline JSON, since `tests/fixtures` only holds real events: unknown type, body that is not JSON, missing `data.object`, charge without `payment_intent`.

## How the fixtures of task 2.2 were captured

The same way as task 1.5: `stripe listen` with the subscribed events forwarding to the local API, the bodies received from `payment-events` by a throwaway script outside the repository, and only the value after `_secret_` in `client_secret` replaced by `redacted` (charges have no `client_secret`).

- `stripe trigger` for `requires_action`, `payment_failed` and `charge.refunded` needs the same `--remove payment_intent:payment_method_types --add automatic_payment_methods...` variant as `payment_intent.succeeded` (see `CLAUDE.md`). `payment_intent.canceled` works without it.
- The trigger refunds the full amount, so `charge.refunded.partial.json` comes from a payment created with `stripe payment_intents create` (2000, `pm_card_visa`, automatic payment methods without redirects) and `stripe refunds create -d payment_intent=pi_... -d amount=500`.
