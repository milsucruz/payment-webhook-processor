# FloodMode

`tools/Simulator/FloodMode.cs`

The simulator's `flood` mode (architecture section 11): it creates a large batch of payments to make the worker scale. Task 4.7's done criterion is that it takes the worker from zero replicas up. Usage: `flood [count]`, 100 by default.

## What it sends

Each payment is one `payment_intent.succeeded` event built in code and posted, signed, to `POST /webhooks/stripe`, like `replay` does (see `docs/code/ReplayMode.md` for `StripeSignature`). Each run draws an 8-character run ID, so the IDs are new on every run: `evt_flood_<run>_<0001>` and `pi_flood_<run>_<0001>`. A rerun creates new payments instead of hitting the inbox as duplicates. Amount and currency are the ones of `pay` (2000 cents, `brl`).

The event carries only the fields the parser reads, plus `object`, `livemode` and `status` so it reads like a Stripe event. A unit test parses it with the API's own `StripeEventParser`, so a change in the parser that breaks it fails the test.

## Why not through Stripe

Section 11 says `flood` creates payments; it doesn't say where. Creating PaymentIntents through the Stripe API, like `pay`, was left out:

- It needs a test secret key, which the project doesn't have yet (pending item of task 2.8, resolved in 4.9).
- The webhooks would reach the cloud only through `stripe listen --forward-to`, signed with the CLI's secret, which would have to be stored in Key Vault. Storing the `whsec_` is task 4.9.
- Scaling depends on how fast messages pile up, which is easier to control without Stripe in the middle.

The cost is that the events are synthetic, not real Stripe payloads. The parser contract is covered by the fixtures in `tests/fixtures`; `flood` only needs events the worker accepts.

## Why `demo_fault=slow`

The worker processes 4 messages at a time (`MaxConcurrentCalls`), each in tens of milliseconds, and KEDA reads the queue length every 30 s. Without a delay, one replica empties the queue between two readings and the scale-out never shows. With `slow`, the first attempt of each delivery waits 10 s (section 6), so one replica handles about 0.4 messages per second and the queue grows while the batch is sent. This is what section 6 says the fault is for: showing scaling without relying on luck. It needs `FaultInjection:Enabled`, which is on in the compose worker and in the cloud sandbox.

With 100 messages, the rule's `ceil(length / 20)` asks for 5 replicas and the maximum of 3 applies.

## Pacing

The webhook's rate limit is 100 requests per 10 s per client IP, and behind the Container Apps ingress every caller shares one bucket (pending item of task 3.6). The mode waits `SendInterval` (125 ms) between sends, so with the response time on top it sends at most 8 per second, 80 of the 100 per window, which leaves room for Stripe's own deliveries. Raising the limit instead would weaken it for every caller. Sends are sequential, like `replay`; parallel sends would need their own pacing and bring nothing here.

At the end it prints how many responses had each status code and exits with `1` if any was not `200`, so a `429` shows up in the output.

## Configuration

| Key | Source |
| --- | --- |
| `Stripe:WebhookSecret` | `dotnet user-secrets` in `tools/Simulator`, or `Stripe__WebhookSecret`. Against the cloud, the value of `stripe-webhook-secret` in Key Vault |
| `Simulator:WebhookUrl` | optional; `http://localhost:8080/webhooks/stripe` by default, `https://<api fqdn>/webhooks/stripe` for the cloud |

## Tests

`tests/Simulator.Tests/FloodModeTests.cs`: the event parses as a paid payment with `DemoFault` `slow`; a run sends `count` deliveries with distinct payments, each accepted by the API's signature verifier; a non-`200` answer gives exit code `1`.
