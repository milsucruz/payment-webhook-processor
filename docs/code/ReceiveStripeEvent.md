# ReceiveStripeEvent

`src/Payments.Application/Webhooks/ReceiveStripeEvent.cs`

Ingestion use case: checks the signature of a Stripe webhook, reads its event ID and publishes it to the event queue. `StripeWebhooksController` calls it (see `docs/code/StripeWebhooksController.md`). This document holds the detail that the code comments leave out. Keep it in sync when the class changes.

## Why a use case

Created in task 1.6. Until then the controller did this work itself and depended on `IWebhookSignatureVerifier`, which lived in Infrastructure. Moving it here sets the layering rule in `CLAUDE.md`: controllers and the worker are adapters, use cases are concrete classes in Application, and their dependencies are ports declared in Application (architecture section 11).

There is no domain here: no entity, no state, no business rule. In DDD terms it is a pure application service. It authenticates, extracts the idempotency key and enqueues. The domain only enters in the worker, through `ProcessStripeEvent`.

It is a concrete class with no interface, registered as a singleton because its three dependencies are singletons too. The API tests use the real class with a fake publisher, so an interface would only exist for the container.

## `ExecuteAsync`

```csharp
Task<ReceiveStripeEventResult> ExecuteAsync(string payload, string? signature, CancellationToken cancellationToken)
```

The parameters carry no HTTP: the caller reads the raw body and the `Stripe-Signature` header and passes their values. The token is `RequestAborted` in the API, but the use case does not know that.

| Step | Fails with | Log |
| --- | --- | --- |
| 1. Check the signature | `InvalidSignature` | Warning with the verifier's reason |
| 2. Read the event ID | `InvalidEventId` | Warning |
| 3. Publish | `PublishFailed` | Warning if the caller cancelled, error otherwise |
| | `Published` | None |

### Why a result enum and not exceptions

The four outcomes are all expected: a bad signature or a broken body is normal input from the internet, and a queue that is down is a known failure that the design answers with `500` (architecture section 4). An enum makes each outcome a value the controller must map, and the `switch` there shows every case. Exceptions would push that decision into the framework, and a global exception handler would log without the event ID.

The failure reason stays in the log and is not part of the result. The controller has no use for it, and nothing about it may reach the response.

### Signature before parsing

The signature is checked over the payload exactly as received, before any deserialization (architecture rule). Only then is the body parsed. An unsigned body is never parsed, and `Invalid_signature_is_reported_before_the_body_is_parsed` covers it with a body that is not JSON.

### Reading the event ID

`IEventPublisher` takes the event ID ready-made (see `docs/code/ServiceBusEventPublisher.md`). `JsonDocument` parses the body and only `id` is read. Nothing else in the event is looked at: parsing the event is the worker's job (task 2.2).

The ID must be a non-empty string of at most 128 characters, the limits of the Service Bus `MessageId`. Its setter throws `ArgumentException` otherwise. The `evt_` prefix is not checked: with a valid signature, the body came from Stripe or from someone who holds the secret, and the prefix is Stripe's format, not a rule of this system.

A body that is not JSON, or has no valid `id`, gives `InvalidEventId`, which the controller answers with `400`. It is a broken request, not a failed infrastructure, so it is not `500`. This does not stop Stripe from resending: Stripe treats **any** non-2xx response as a failed delivery, `4xx` included. The choice between `400` and `500` is about meaning and logs, not retries. In practice a real Stripe event always has an `id`.

`JsonDocument` builds the whole document only to read one property. A forward-only `Utf8JsonReader` that stops at the top-level `id` would allocate less, but it is longer and harder to read, and events are a few KB. Code review during task 1.4 raised it; it was dropped because readable code is worth more here than a gain nobody can measure.

### Publishing

The payload, the same string the signature was checked over, is published with the event ID (architecture section 4, step 3). `Published` is returned only after `PublishAsync` returns, so the controller's `200` means the event is on the queue.

Any exception from the publisher is caught, logged as an error with the event ID, and returned as `PublishFailed`. Catching `Exception` is deliberate: whatever the publisher throws, the answer to Stripe is the same, and Stripe resends.

### Cancellation

If Stripe gives up and closes the connection, the token is cancelled and the publish is cancelled with it. If the message was already sent when that happens, Stripe resends anyway and the worker's inbox discards the duplicate (section 6), so cancelling is safe.

That cancellation raises `OperationCanceledException`, which the general catch would log as an error, as if the queue were down. A separate catch, filtered on `cancellationToken.IsCancellationRequested`, logs it as a warning instead, so error logs (and the publish-failure metrics of section 9) only count real queue failures. The warning says "cancelled by the caller", not "Stripe closed the connection": the use case does not know where the token comes from, and another caller, such as host shutdown, could cancel it too. It still returns `PublishFailed`: for the caller both mean "not published", and the `500` goes to a client that already left. An `OperationCanceledException` without the token being cancelled, such as an SDK timeout, is a real failure and falls through to the general catch. Found in code review during task 1.4.

## Tests

`tests/Payments.Application.Tests/Webhooks` tests the class with no HTTP and no Infrastructure: a fake verifier that returns a fixed result, fake publishers (recording, throwing, observing the token) and a logger that collects the level and the formatted message of each entry. They cover the four results, the event ID cases (missing, empty, blank, not a string, not an object, not JSON, over 128 characters), the signature checked before parsing, a cancelled publish logged as a warning and a cancellation the caller did not request logged as an error. The publish logs are checked for the event ID and the signature log for the verifier's reason, since keeping them in the log is the reason for the result enum.

`RecordingEventPublisher` is also linked into `tests/Payments.Api.Tests` with `<Compile Link>`, the same way `StripeSignatureHelper` is shared, so there is one copy of it.

The real signature check is tested in `tests/Payments.Infrastructure.Tests` (see `docs/code/StripeWebhookSignatureVerifier.md`), and the whole HTTP path in `tests/Payments.Api.Tests`.
