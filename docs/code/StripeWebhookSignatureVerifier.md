# StripeWebhookSignatureVerifier

`src/Payments.Infrastructure/Webhooks/StripeWebhookSignatureVerifier.cs`

Checks the `Stripe-Signature` header of a webhook against the raw body. This document holds the detail that the code comments leave out. Keep it in sync when the class changes.

## Where it lives

Architecture section 11 puts Stripe signature validation in Infrastructure. The interface `IWebhookSignatureVerifier` sits next to it, not in Application: Application holds the ports of the use case, which the worker also runs, and the signature is only checked at the API edge. The interface keeps the controller free of Stripe.net types.

## `ValidateSignature`, not `ConstructEvent`

Stripe.net offers two calls:

- `EventUtility.ConstructEvent` checks the signature and then deserializes into `Stripe.Event`. By default it also throws when the event's API version differs from the one the library is pinned to.
- `EventUtility.ValidateSignature` only checks the HMAC and the timestamp.

The API forwards the original JSON and never needs a typed event, so `ValidateSignature` is enough. `ConstructEvent` would also turn an API version mismatch between the Stripe account and the library into a `400`, which is not a signature problem.

## What the check does

Stripe signs `"{timestamp}.{body}"` with HMAC-SHA256 and the `whsec_...` secret, and sends `t=<timestamp>,v1=<hex signature>`. The library:

1. Parses the header. A missing `t` or `v1` is a malformed header.
2. Compares each `v1` with the expected signature, in constant time. One match is enough.
3. Rejects the request if the timestamp is more than `ToleranceSeconds` (300 s, architecture section 8) away from now. This limits replay of a captured request.

Point 2 is what makes secret rotation work with one configured secret. During a rotation Stripe sends one `v1` per active secret, so the new secret matches even while the old one is still in use (section 8).

## `TimeProvider`

The current time comes from the injected `TimeProvider` (`TimeProvider.System` in the API) and is passed to the library as `utcNow`. Tests use a fixed clock, which allows exact boundary cases (signed 300 s ago is valid, 301 s ago is not) without depending on the real clock.

## `SignatureVerificationResult`

The library reports every failure as a `StripeException`, and only the message tells them apart. The result carries that message as `FailureReason`, unchanged, instead of an enum built by matching message text, which would break if Stripe reworded it. A missing header is checked before calling the library and has its own reason. The controller logs the reason; the caller only sees `400`.

## The secret

`StripeOptions.WebhookSecret`, from the `Stripe:WebhookSecret` configuration key. The API refuses to start without it (`ValidateOnStart` in `Program.cs`): an endpoint with no secret would reject every webhook, and the failure is clearer at startup.

| Where | Source |
| --- | --- |
| `dotnet run` | `dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."` in `src/Payments.Api` |
| `docker compose` | `STRIPE_WEBHOOK_SECRET` in `.env`, passed to the API as `Stripe__WebhookSecret` |
| Azure | Key Vault secret `stripe-webhook-secret` (architecture section 8) |

Locally, the value is the `whsec_...` that `stripe listen` prints.

## Tests

`tests/Payments.Infrastructure.Tests/Webhooks`. The tests build signatures with their own HMAC helper that follows Stripe's published scheme, not with Stripe.net, so they check the library against the spec instead of against itself. The test secret is a fake `whsec_` literal: the tests sign with it too, so any value works. The case with two `v1` values, where only one matches, covers the secret rotation described above.
