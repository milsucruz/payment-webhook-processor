# StripeWebhooksController

`src/Payments.Api/Controllers/StripeWebhooksController.cs`

Receives Stripe webhooks at `POST /webhooks/stripe`. This document holds the detail that the code comments leave out. Keep it in sync when the class changes.

## `MaxBodyBytes`

The 256 KB body limit comes from architecture section 8 and is applied with `[RequestSizeLimit]`, so a larger request gets `413`.

The Service Bus Basic message cap is also 256 KB, but it counts the body **and** the message headers. A body close to the limit passes the API and then fails to publish with `MessageSizeExceeded`, which answers `500` like any other publish failure. Task 1.4 kept the 256 KB limit instead of lowering it: real `payment_intent` events are a few KB, and any lower limit would be a made-up number. Section 8 of the architecture says the same.

## Rate limit

Section 8 protects the endpoint from floods of unsigned requests with the ASP.NET Core rate limiter (task 3.4). The controller carries `[EnableRateLimiting("webhook")]`, and `Program.cs` defines the policy:

| Setting | Value | Why |
| --- | --- | --- |
| Algorithm | Fixed window | The simplest one; the goal is a ceiling, not smoothing |
| Limit | 100 requests per 1 s (`RateLimiting:Webhook:PermitLimit` and `Window`) | Far above real traffic: a payment produces a handful of events, and Stripe's own test-mode API limit keeps even the `flood` mode of task 4.7 below it |
| Partition | One, global | Behind the Container Apps ingress the remote address is the proxy's, so a per-IP limit would need forwarded headers first; with a single API replica a global limit is the same as a per-replica one |
| Queue | 0 | A queued request still holds a connection; rejecting at once is what a flood needs |
| Rejection | `429` | The framework default is `503`. Stripe resends on any non-2xx, so a real event rejected in a flood is delivered again later |

The limit runs before the action, so a rejected request never reaches the signature check or the queue. Signed requests count too: in a flood, Stripe's events wait for the resend like everyone else's.

The values are options, not constants (unlike the publish retry below), so that the test can set 2 requests per minute and get a deterministic `429`; with the real 100 per second a test would depend on how fast the machine sends. They are validated at startup (positive limit and window). The policy reads them from `IOptions` when the limiter is first created, so the settings from `WebApplicationFactory` apply.

Measured in task 3.4 against compose: 300 concurrent unsigned requests from one `HttpClient` gave exactly 100 `400` and 200 `429`. Sequential `curl` processes on Windows reach only about 37 requests per second and never hit the limit.

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

### Calling `ReceiveStripeEvent` and mapping the result

The action passes the raw body, the `Stripe-Signature` header and `HttpContext.RequestAborted` to the `ReceiveStripeEvent` use case (see `docs/code/ReceiveStripeEvent.md`). The use case checks the signature, reads the event ID, publishes and logs. The action only maps its result to a status code:

| Result | Status | Why |
| --- | --- | --- |
| `Published` | `200` | The event is on the queue |
| `InvalidSignature` | `400` | Architecture section 4 |
| `InvalidEventId` | `400` | A broken request, not a failed infrastructure |
| `PublishFailed` | `500` | Section 4, failure table: Stripe resends with backoff |

All responses have no body, so a caller probing the endpoint learns nothing; the reason is only in the log.

The `switch` names every value and ends with `throw new UnreachableException()`. The compiler requires a default arm for an enum, and throwing makes a new result value fail loudly instead of falling into a status code by accident.

### Why the controller has no `try`/`catch` and no JSON parsing

Until task 1.5 the action did all of this itself: it parsed the `id` with `JsonDocument`, called the publisher inside a `try`/`catch`, and told the two kinds of `OperationCanceledException` apart. Task 1.6 moved that into the use case, after the layering rule in `CLAUDE.md`: a controller translates HTTP and nothing else. The controller no longer references `Payments.Infrastructure`, and the rules of ingestion are tested without HTTP.

If Stripe closes the connection while the body is still being read, the read throws out of the action. Task 3.4 measured what happens, with the API in compose and a raw socket that announced `Content-Length: 1000`, sent 20 bytes and closed:

- **Production** (the cloud): Kestrel throws `BadHttpRequestException` ("Unexpected end of request content"), not `OperationCanceledException`, and logs it at `Debug` in `Microsoft.AspNetCore.Server.Kestrel.BadRequests`. Nothing is written at the configured levels.
- **Development**: the developer exception page catches it, logs "The request was aborted by the client" at `Debug` and records status `499`.

Neither is an error, so no pipeline-level handler was added, and the controller keeps no `try`/`catch`.

The action was renamed from `Receive` to `ReceiveAsync` in task 1.2, after the naming rule in `CLAUDE.md`. The route comes from `[Route]`, so the `Async` suffix doesn't change the URL.

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

`tests/Payments.Api.Tests` runs the real pipeline with `WebApplicationFactory`, with the real `ReceiveStripeEvent`, and replaces `IEventPublisher` with a fake: one that records the calls (linked from `Payments.Application.Tests`), and one that throws `ServiceBusException`. The signature is computed by `StripeSignatureHelper`, linked from `Payments.Infrastructure.Tests` so both projects sign the same way. The tests cover: valid signature (`200`, one publish with the same ID and body), failing publisher (`500`), invalid signature, a body without a valid `id` (missing, empty, blank, not a string, not an object, not JSON), and an ID over 128 characters (`400`, nothing published). Since task 3.4 another test sets the limit to 2 per minute and checks that the third request gets `429` and publishes nothing, with an unsigned request counting toward the limit. A last test blocks the publish, cancels the request from the client, and checks that a warning is logged, not an error. `TestServer` turns the client's cancellation into `RequestAborted`, so this is the same path as Stripe closing the connection. Since task 1.6 that warning comes from the `ReceiveStripeEvent` log category; the test's assertions did not change.

The same rules are also tested without HTTP in `tests/Payments.Application.Tests`. The API tests stay because they prove the status codes and the raw-body path through the real pipeline.

`ControllerDependencyTests` checks by reflection that no controller takes or holds a type from the `Payments.Infrastructure` assembly, so the layering rule cannot be broken silently. It is the controller counterpart of `DomainDependencyTests`.

The trip to a real queue is not repeated here: the integration test of `ServiceBusEventPublisher` covers it, and task 1.5 runs the whole path with `stripe listen`.
