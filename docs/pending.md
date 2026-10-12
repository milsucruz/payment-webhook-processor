# Pending items

Open questions found during a task that are out of its scope. Review this file at the end of every milestone: resolve each item, move it to a task, or drop it with a reason.

| Found in | Item | Resolve by |
| --- | --- | --- |
| 1.3 | The `Payments.IntegrationTests` project is kept out of CI by a `FullyQualifiedName` filter in `ci.yml`. Remove the filter when integration tests join CI. | 3.5 |
| 1.3 | `ServiceBusEventPublisherTests` starts its own emulator and SQL Server per test through `IAsyncLifetime`. With one test that is fine; when more integration tests arrive, share the containers through a class or collection fixture. | 3.5 |
| 1.4 | `ServiceBusOptions` only has `ConnectionString`, and `ValidateOnStart` requires it. With managed identity the API needs the namespace and `DefaultAzureCredential` instead, and the validation must accept either. Until then the API cannot start in the cloud without a key-based connection string, which section 8 forbids. | 4.6 |
| 1.4 | The publish `TryTimeout` is 2 s (see `docs/code/StripeWebhooksController.md`). After the API scales from zero, the first send also opens the AMQP connection and authenticates, which may take longer against a cloud namespace and fail every attempt. Measure the first publish after a cold start and raise `TryTimeout` if needed. | 4.7 |
| 1.5 | `tests/fixtures` only has `payment_intent.created` and `payment_intent.succeeded`, the events `stripe trigger payment_intent.succeeded` delivers. The parser needs real payloads of the other subscribed types too (`payment_intent.processing`, `.requires_action`, `.payment_failed`, `.canceled` and `charge.refunded`, whose amounts differ in shape). Capture them with `stripe trigger` the same way (body read from the queue, only the `client_secret` value masked). | 2.2 |
