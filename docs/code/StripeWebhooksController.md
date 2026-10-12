# StripeWebhooksController

`src/Payments.Api/Controllers/StripeWebhooksController.cs`

Receives Stripe webhooks at `POST /webhooks/stripe`. This document holds the detail that the code comments leave out. Keep it in sync when the class changes.

## `MaxBodyBytes`

The 256 KB body limit comes from architecture section 8 and is applied with `[RequestSizeLimit]`, so a larger request gets `413`.

The Service Bus Basic message cap is also 256 KB, but it counts the body **and** the message headers. A body close to the limit passes the API and then fails to publish with `MessageSizeExceeded`, which answers `500` like any other publish failure. Task 1.4 kept the 256 KB limit instead of lowering it: real `payment_intent` events are a few KB, and any lower limit would be a made-up number. Section 8 of the architecture says the same.

## `ReceiveAsync`

### Why the action has no parameters

Any bound parameter, even a `CancellationToken`, makes MVC build value providers for the action. The form value provider reads a form-encoded body (`application/x-www-form-urlencoded`) before the action runs, and leaves nothing for the raw read. With no parameters, MVC does not touch the body.

The request's cancellation token comes from `HttpContext.RequestAborted` instead. This is the exception to the "async methods take a `CancellationToken`" rule in `CLAUDE.md`.

Dependencies such as the signature verifier and the logger come through the constructor, which MVC does not bind from the request, so they don't break this.

This was found in code review during task 1.1. It replaced a first workaround with `[Consumes]`, which only restricted the content type and did not fix the cause.

### Why the body is read as raw text

The Stripe signature is checked over the body exactly as received, before any deserialization (architecture rule: signature over the raw body). Reading it as text through `StreamReader` keeps the bytes as UTF-8 text with no reformatting.

Reading the stream also enforces the size limit for chunked requests. Those have no `Content-Length`, so the server can't reject them upfront. It fails the read once the body passes the limit.

### `leaveOpen: true`

The request stream belongs to the server, not to the reader. Without `leaveOpen`, disposing the `StreamReader` would also dispose `Request.Body`.

### Signature check and `400`

The `Stripe-Signature` header and the raw body go to `IWebhookSignatureVerifier` (see `docs/code/StripeWebhookSignatureVerifier.md`). If the check fails, the action logs a warning with the reason and returns `400` (architecture section 4). The reason stays in the log, never in the response, so a caller probing the endpoint learns nothing. The body is not logged: it is unauthenticated input and could be up to 256 KB.

The action was renamed from `Receive` to `ReceiveAsync` in task 1.2, after the naming rule in `CLAUDE.md`. The route comes from `[Route]`, so the `Async` suffix doesn't change the URL.

### Reading the event ID and `400`

`IEventPublisher` takes the event ID ready-made (see `docs/code/ServiceBusEventPublisher.md`), so the action reads it after the signature check. `JsonDocument` parses the body and only `id` is read. Nothing else in the event is looked at here: parsing the event is the worker's job (task 2.2).

The ID must be a non-empty string of at most 128 characters, the limits of the Service Bus `MessageId`. Its setter throws `ArgumentException` otherwise. The `evt_` prefix is not checked: with a valid signature, the body came from Stripe or from someone who holds the secret, and the prefix is Stripe's format, not a rule of this system.

A body that is not JSON, or has no valid `id`, gets `400` with the reason in the log, like a bad signature. It is a broken request, not a failed infrastructure, so it is not `500`. This does not stop Stripe from resending: Stripe treats **any** non-2xx response as a failed delivery, `4xx` included. The choice between `400` and `500` is about meaning and logs, not retries. In practice a real Stripe event always has an `id`.

`JsonDocument` builds the whole document only to read one property. A forward-only `Utf8JsonReader` that stops at the top-level `id` would allocate less, but it is longer and harder to read, and events are a few KB. Code review during task 1.4 raised it; it was dropped because readable code is worth more here than a gain nobody can measure.

### Publishing and `500`

The raw body, the same string the signature was checked over, is published with the event ID (architecture section 4, step 3). The action answers `200` only after `PublishAsync` returns, so a `200` means the event is on the queue.

Any exception from the publisher is caught, logged as an error with the event ID, and turned into `500` with no body (section 4, failure table: Stripe resends with backoff). The catch is explicit instead of letting the exception reach the framework, for two reasons: the rule "publish failed, answer `500`" is visible in the action, and in `Development` the developer exception page would otherwise send the stack trace to the caller.

The publish uses `HttpContext.RequestAborted`. If Stripe gives up and closes the connection, the publish is cancelled. If the message was already sent when that happens, Stripe resends anyway and the worker's inbox discards the duplicate (section 6), so cancelling is safe.

That cancellation raises `OperationCanceledException`, which the general catch would log as an error, as if the queue were down. A separate catch, filtered on `RequestAborted.IsCancellationRequested`, logs it as a warning instead, so error logs (and the publish-failure metrics of section 9) only count real queue failures. It still returns `500`, which nobody receives. An `OperationCanceledException` without the request being aborted, such as an SDK timeout, is a real failure and falls through to the general catch. Found in code review during task 1.4.

### How long a failed publish can take

The `ServiceBusClient` is created in `Program.cs` with these retry options:

| Option | Value | SDK default |
| --- | --- | --- |
| `Mode` | `Exponential` | `Exponential` |
| `MaxRetries` | 2 | 3 |
| `Delay` | 0.5 s | 0.8 s |
| `MaxDelay` | 2 s | 60 s |
| `TryTimeout` | 2 s | 60 s |

With the defaults, a queue that does not answer holds the request for minutes. With these values the worst case is about 8 s (three attempts of 2 s plus the waits), so the API answers `500` before Stripe gives up on the request. Stripe does not publish its exact timeout; it only asks endpoints to answer quickly. The values are fixed in code, not configuration, because no environment needs different ones.

### Tests

`tests/Payments.Api.Tests` runs the real pipeline with `WebApplicationFactory` and replaces `IEventPublisher` with a fake: one that records the calls, and one that throws `ServiceBusException`. The signature is computed by `StripeSignatureHelper`, linked from `Payments.Infrastructure.Tests` so both projects sign the same way. The tests cover: valid signature (`200`, one publish with the same ID and body), failing publisher (`500`), invalid signature, a body without a valid `id` (missing, empty, blank, not a string, not an object, not JSON), and an ID over 128 characters (`400`, nothing published). A last test blocks the publish, cancels the request from the client, and checks that the controller logs a warning, not an error. `TestServer` turns the client's cancellation into `RequestAborted`, so this is the same path as Stripe closing the connection.

The trip to a real queue is not repeated here: the integration test of `ServiceBusEventPublisher` covers it, and task 1.5 runs the whole path with `stripe listen`.
