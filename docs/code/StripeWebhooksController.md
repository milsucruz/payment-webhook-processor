# StripeWebhooksController

`src/Payments.Api/Controllers/StripeWebhooksController.cs`

Receives Stripe webhooks at `POST /webhooks/stripe`. This document holds the detail that the code comments leave out. Keep it in sync when the class changes.

## `MaxBodyBytes`

The 256 KB body limit comes from architecture section 8 and is applied with `[RequestSizeLimit]`, so a larger request gets `413`.

The Service Bus Basic message cap is also 256 KB, but it counts the body **and** the message headers. A body close to the limit passes the API and then fails to publish with `MessageSizeExceeded`, which answers `500` like any other publish failure. Task 1.4 kept the 256 KB limit instead of lowering it: real `payment_intent` events are a few KB, and any lower limit would be a made-up number. Section 8 of the architecture says the same.

## Rate limit

Task 3.6 put the ASP.NET Core rate limiter on the action with `[EnableRateLimiting(WebhookRateLimitOptions.PolicyName)]` (architecture section 8). The policy and `app.UseRateLimiter()` are in `Program.cs`. The limiter is middleware, so it rejects a request before MVC runs: the body is not read and the signature is not checked, so a flood of unsigned requests costs almost nothing, and those requests count toward the limit like any other.

| Setting | Value | Why |
| --- | --- | --- |
| Algorithm | Fixed window | What task 3.6 asks for, and the simplest to explain: a counter that resets every window. The cost: up to twice the limit gets through around a boundary (100 at 9.9 s and 100 at 10.1 s). A sliding window or token bucket would smooth that, which is not needed for a flood guard |
| `PermitLimit` / `Window` | 100 per 10 s | Test-mode traffic is a few events per payment, so real deliveries stay far below it. Stripe resends on `429`, so a limit that is too low delays events instead of losing them |
| Partition | One per client IP (`RemoteIpAddress`) | A single bucket for everyone would let anyone use up the budget and get Stripe's own deliveries rejected, while an unsigned request without a limiter costs only a failed HMAC. With one bucket per IP, a flood from one source only limits that source |
| `QueueLimit` | 0 | A request over the limit is rejected at once instead of holding a connection open |
| `RejectionStatusCode` | `429` | The middleware's default is `503`, which reads as the API being down |

Behind the Container Apps ingress, `RemoteIpAddress` is the proxy's, so until the API trusts the ingress's `X-Forwarded-For` (`docs/pending.md`, task 4.9) every caller shares the proxy's bucket and the limit acts as a single one. That is no worse than a single partition, and becomes per-client with the forwarded headers. A request with no IP (as in `TestServer`) uses the empty key.

A delivery rejected with `429` never reaches Service Bus, so it is not in the dead-letter. Stripe resends it, in the sandbox only three times over a few hours (architecture section 4), and after that it can only be resent from the Stripe dashboard.

Only the webhook has the policy. The admin endpoints already require `X-Admin-Key`, and a global limiter would also limit `/health/live`, which the probes of task 4.7 call.

`app.UseRouting()` is called explicitly before `app.UseRateLimiter()`. The limiter finds the policy in the endpoint's metadata, so it must run after routing has selected the endpoint. `WebApplication` adds routing on its own at the start of the pipeline, but making it explicit keeps a middleware added later from moving routing after the limiter and switching the limiter off without any error.

The values come from the `WebhookRateLimit` configuration section (`appsettings.json`), validated on start, instead of constants. The reason is the tests: `FixedWindowRateLimiter` takes no `TimeProvider`, so a test can't move the clock, and with a real 10 s window a test could cross a window reset and become flaky. The tests set a limit of 1 or 2 and a 1 h window instead. The policy reads `IOptions<WebhookRateLimitOptions>` from `RequestServices` so that it gets the validated options, including the tests' overrides; that is one singleton lookup per request. The partitioned limiter keeps one `FixedWindowRateLimiter` per IP and drops idle ones, so the factory runs again for an IP after it has been idle.

The rejection is not logged per request: a flood would become a flood of log lines. ASP.NET Core already reports rejections in the `aspnetcore.rate_limiting` metrics, which telemetry in M5 can use.

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

If Stripe closes the connection while the body is still being read, `ReadToEndAsync` throws `OperationCanceledException` out of the action. The rule keeps `try`/`catch` out of the controller, so this belongs to a pipeline-level handler; it is in `docs/pending.md` for task 3.4.

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

`tests/Payments.Api.Tests` runs the real pipeline with `WebApplicationFactory`, with the real `ReceiveStripeEvent`, and replaces `IEventPublisher` with a fake: one that records the calls (linked from `Payments.Application.Tests`), and one that throws `ServiceBusException`. The signature is computed by `StripeSignatureHelper`, linked from `Payments.Infrastructure.Tests` so both projects sign the same way. The tests cover: valid signature (`200`, one publish with the same ID and body), failing publisher (`500`), invalid signature, a body without a valid `id` (missing, empty, blank, not a string, not an object, not JSON), and an ID over 128 characters (`400`, nothing published). A last test blocks the publish, cancels the request from the client, and checks that a warning is logged, not an error. `TestServer` turns the client's cancellation into `RequestAborted`, so this is the same path as Stripe closing the connection. Since task 1.6 that warning comes from the `ReceiveStripeEvent` log category; the test's assertions did not change. Task 3.6 added four rate limit tests, each with its own host and so its own limiter: signed requests over the limit get `429` and are not published; unsigned requests use up the limit, so a signed one after them gets `429`; a second client IP still gets through when the first used up its limit; and `/health/live` still answers `200` with the webhook limit used up. `TestServer` has no real connection, so `ClientIpStartupFilter` sets `RemoteIpAddress` from a test-only header.

The same rules are also tested without HTTP in `tests/Payments.Application.Tests`. The API tests stay because they prove the status codes and the raw-body path through the real pipeline.

`ControllerDependencyTests` checks by reflection that no controller takes or holds a type from the `Payments.Infrastructure` assembly, so the layering rule cannot be broken silently. It is the controller counterpart of `DomainDependencyTests`.

The trip to a real queue is not repeated here: the integration test of `ServiceBusEventPublisher` covers it, and task 1.5 runs the whole path with `stripe listen`.
