# StripeWebhooksController

`src/Payments.Api/Controllers/StripeWebhooksController.cs`

Receives Stripe webhooks at `POST /webhooks/stripe`. This document holds the detail that the code comments leave out. Keep it in sync when the class changes.

## `MaxBodyBytes`

The 256 KB body limit comes from architecture section 8 and is applied with `[RequestSizeLimit]`, so a larger request gets `413`.

The Service Bus Basic message cap is also 256 KB, but it counts the body **and** the message headers. A body close to the limit can pass the API and still not fit in a message. This is tracked in `docs/pending.md` and decided in task 1.3 or 1.4.

## `Receive`

### Why the action has no parameters

Any bound parameter, even a `CancellationToken`, makes MVC build value providers for the action. The form value provider reads a form-encoded body (`application/x-www-form-urlencoded`) before the action runs, and leaves nothing for the raw read. With no parameters, MVC does not touch the body.

The request's cancellation token comes from `HttpContext.RequestAborted` instead. This is the exception to the "async methods take a `CancellationToken`" rule in `CLAUDE.md`.

This was found in code review during task 1.1. It replaced a first workaround with `[Consumes]`, which only restricted the content type and did not fix the cause.

### Why the body is read as raw text

The Stripe signature is checked over the body exactly as received, before any deserialization (architecture rule: signature over the raw body). Reading it as text through `StreamReader` keeps the bytes as UTF-8 text with no reformatting.

Reading the stream also enforces the size limit for chunked requests. Those have no `Content-Length`, so the server can't reject them upfront. It fails the read once the body passes the limit.

### `leaveOpen: true`

The request stream belongs to the server, not to the reader. Without `leaveOpen`, disposing the `StreamReader` would also dispose `Request.Body`.

### Current state

The payload is read and discarded. Task 1.2 validates the signature over it and task 1.4 publishes it to the queue. Task 1.2 also renames the action to `ReceiveAsync` (see `docs/pending.md`).
