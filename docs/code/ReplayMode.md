# ReplayMode

`tools/Simulator/ReplayMode.cs`, with `ReplayPlan.cs` and `StripeSignature.cs`

The simulator's `replay` mode (architecture section 11, "Simulator"). It resends every event already in the inbox to `POST /webhooks/stripe`, each one twice, in shuffled order, with a fresh signature. Task 2.8's done criterion is that this changes the state of no payment. This document holds the detail that the code leaves out. Keep it in sync when the classes change.

## Where the events come from

`InboxEvents.Payload` holds the original JSON of every processed event (section 5, "for auditing and reprocessing"). Replay reads that column through `PaymentsDbContext`, so the table and column names live in one place, the EF Core model the worker writes with.

Alternatives left out:

- **Resending from Stripe** (`stripe events resend` or the dashboard): one event per call, in order, and needs the Stripe account. The inbox has every event locally.
- **The fixtures in `tests/fixtures`:** they are events of payments that may not be in the local database, so the run would create payments instead of proving that nothing changes.
- **Raw `SqlClient`:** one query is enough, but it repeats the table name and column name the model already has.

The simulator is a tool, not part of the layered application, so it uses the `DbContext` directly instead of a port. The rule in `CLAUDE.md` that only `Program.cs` references `Payments.Infrastructure` is about the API and the worker.

Only processed events are in the inbox: an impossible conflict rolls back and writes nothing (section 4, step 6). So every delivery of a replay is a duplicate for the worker.

## Doubled and shuffled: `ReplayPlan`

`ReplayPlan.Build` puts every payload in the list twice and shuffles it with `Random.Shuffle`. It is a pure function of the payloads and the `Random`, which is what the unit tests check: each payload appears exactly twice, the order differs from the input, and the same seed gives the same order.

The seed is printed at the start of each run, and `replay <seed>` repeats that order. Without it a run that went wrong could not be reproduced.

Since every event is already in the inbox, replay exercises the duplicate path, not the levels: no delivery reaches the transition table. Out-of-order delivery of new events is proved by the property test of task 2.7, which feeds every permutation through the use case.

## Signing: `StripeSignature`

The API checks `Stripe-Signature` with a 300 s tolerance (see `docs/code/StripeWebhookSignatureVerifier.md`), so the signature Stripe sent originally is useless: it is older than five minutes, and the inbox doesn't store it. Each delivery is signed again just before it is sent, with the current timestamp, following Stripe's scheme: `t=<timestamp>,v1=<hex HMAC-SHA256 of "{timestamp}.{payload}">` with the `whsec_...` secret.

The secret is the same one the API uses: `Stripe:WebhookSecret`, the `STRIPE_WEBHOOK_SECRET` of `.env` locally. A replay against the cloud would need the cloud endpoint's secret, which stays in Key Vault; the simulator is meant for the local environment.

The payload is sent as the exact string read from the inbox, encoded as UTF-8. The API validates the signature over the raw body, so any change, such as re-serializing the JSON, would break the signature.

The unit tests sign with `StripeSignature` and verify with the API's own `StripeWebhookSignatureVerifier`, so they prove the two sides agree. The payload in the test has a non-ASCII character, which would catch an encoding mismatch.

## Sending

Deliveries go one at a time, through one `HttpClient`. The order is the point of the test, and sending in parallel would only add a second source of reordering. At the end the mode prints how many responses had each status code and exits with `1` if any was not `200`.

## Configuration

| Key | Source |
| --- | --- |
| `ConnectionStrings:Payments` | `dotnet user-secrets` in `tools/Simulator`, or `ConnectionStrings__Payments` |
| `Stripe:WebhookSecret` | the same, or `Stripe__WebhookSecret` |
| `Simulator:WebhookUrl` | optional; `http://localhost:8080/webhooks/stripe` by default |

## Verification

With the compose stack up, task 2.8 snapshotted `Payments` (status, amounts, currency, `LastEventId`, `UpdatedAt`, `RowVersion`) and the inbox and history counts, ran `replay`, waited for the worker, and snapshotted again. The two snapshots were identical, including `RowVersion`, which changes on any update. The worker logged "already processed" once per delivery.
