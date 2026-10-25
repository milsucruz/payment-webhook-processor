# Generating payments

The dashboard is read-only: it has no button to create or delete a payment. Payments belong to Stripe, and this system only mirrors the events Stripe sends (section 3 of [architecture.md](architecture.md)). To see new payments on the dashboard, make Stripe, or something that signs events the way Stripe does, call the webhook.

## Before you start

The local environment must be up (`docker compose up -d`, see `CLAUDE.md`), with the dashboard at <http://localhost:8080/>. Then pick one of the two ways below.

| | Real Stripe events | Signed synthetic events |
| --- | --- | --- |
| Tools | `stripe listen` plus `stripe trigger`, or the simulator's `pay` and `refund` | The simulator's `flood` |
| Needs | The Stripe CLI logged in to the project's sandbox; `pay` and `refund` also need its `sk_test_` key | Only the webhook secret from `.env` |
| Good for | Real payloads, declines, authentication, refunds | Many payments at once, and the demo faults |

## Real Stripe events

### 1. Forward Stripe to the local API

Keep this running in its own terminal:

```bash
stripe listen --events payment_intent.created,payment_intent.processing,payment_intent.requires_action,payment_intent.succeeded,payment_intent.payment_failed,payment_intent.canceled,charge.refunded --forward-to localhost:8080/webhooks/stripe
```

`STRIPE_WEBHOOK_SECRET` in `.env` must be the value of `stripe listen --print-secret`; after changing it, run `docker compose up -d api`.

The cloud endpoint registered in the same sandbox also receives these events. To keep them local, disable it first: `stripe webhook_endpoints update <we_...> -d disabled=true` (and `disabled=false` to turn it back on).

### 2. Create payments

One successful payment, with the Stripe CLI:

```bash
stripe trigger payment_intent.succeeded --remove payment_intent:payment_method_types --add "payment_intent:automatic_payment_methods[enabled]=true" --add "payment_intent:automatic_payment_methods[allow_redirects]=never"
```

Three payments, one per test card, with the simulator. It needs the sandbox's secret key once (it refuses any key that isn't a test key):

```bash
dotnet user-secrets set Stripe:SecretKey <sk_test_...> --project tools/Simulator
dotnet run --project tools/Simulator -- pay
```

| Card | Ends as |
| --- | --- |
| `approve` | Paid |
| `decline` | Failed |
| `authenticate` | Requires action |

`pay` prints each payment ID (`pi_...`).

### 3. Refund a payment

```bash
dotnet run --project tools/Simulator -- refund <pi_...> 500   # partial: 500 cents
dotnet run --project tools/Simulator -- refund <pi_...>       # the rest
```

The payment moves to Partially refunded and then to Refunded.

## Signed synthetic events (`flood`)

`flood` builds `payment_intent.succeeded` events in code, signs them with the webhook secret and posts them to the local API at 8 per second. It doesn't talk to Stripe, so it needs no `stripe listen` and no `sk_test_` key. Set the secret once, the same value as `STRIPE_WEBHOOK_SECRET` in `.env`:

```bash
dotnet user-secrets set Stripe:WebhookSecret <STRIPE_WEBHOOK_SECRET from .env> --project tools/Simulator
dotnet run --project tools/Simulator -- flood 20          # 20 payments, each delayed 10 s by the worker (slow, the default)
dotnet run --project tools/Simulator -- flood 5 poison    # 5 payments that end in the dead-letter
```

Each run uses new IDs (`pi_flood_<run>_0001`, ...), so a rerun creates new payments instead of duplicates. The fault is one of `slow`, `transient` or `poison` (below); the compose worker has fault injection turned on.

## What to show in a demo

| To see | Do | Where it shows |
| --- | --- | --- |
| A payment arriving live | `stripe trigger ...` or `pay` | Live screen, then Payments |
| An event out of order | `stripe trigger ...`: Stripe often delivers `payment_intent.created` after `succeeded` | Payment detail: "Ignored: out of order" |
| A duplicate | `stripe events resend <evt_...>`, or `dotnet run --project tools/Simulator -- replay` (resends every event in the inbox twice; needs the connection string, see `CLAUDE.md`) | Live screen: Duplicate; the state doesn't change |
| Retry of a transient error | `flood 1 transient`, or a `stripe trigger ...` with `--add "payment_intent:metadata[demo_fault]=transient"` | Applied on the third attempt of the first delivery |
| The dead-letter | `flood 1 poison`, or the same `demo_fault=poison` | Dead-letter screen after 5 deliveries; Reprocess needs `ADMIN_API_KEY` from `.env` |
| A refund | `refund <pi_...> [cents]` | Payment detail: Partially refunded, Refunded |

## Starting from an empty database

There is no delete. Locally, recreate the volumes:

```bash
docker compose down -v
docker compose up -d
```

This erases the local database and the emulator's queues; the migrator creates the schema again.

## Why the dashboard can't create or delete

- **Create:** a row written straight to the database would not exist in Stripe, and the next real event would disagree with it. Creating the payment in Stripe from the dashboard would put a key that can charge cards in a public API, which today holds only the webhook secret and the admin key (section 8). That is why `pay` lives in the simulator, on the developer's machine.
- **Delete:** the inbox is what makes a resent event a duplicate (section 6); deleting its row would let the same event apply again. The history is the record of every event applied or ignored. A payment doesn't go away, it changes state: cancel or refund it in Stripe, and the event updates it here.
