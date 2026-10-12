# Pending items

Open questions found during a task that are out of its scope. Review this file at the end of every milestone: resolve each item, move it to a task, or drop it with a reason.

| Found in | Item | Resolve by |
| --- | --- | --- |
| 1.3 | The `Payments.IntegrationTests` project is kept out of CI by a `FullyQualifiedName` filter in `ci.yml`. Remove the filter when integration tests join CI. | 3.5 |
| 1.3 | `ServiceBusEventPublisherTests` starts its own emulator and SQL Server per test through `IAsyncLifetime`. With one test that is fine; when more integration tests arrive, share the containers through a class or collection fixture. | 3.5 |
| 1.4 | `docker-compose.yml` makes the API wait for the `servicebus` container to start, not for the emulator to be ready (it has no healthcheck). Webhooks in the first seconds after `docker compose up` get `500` until the emulator loads. Add a healthcheck if the image has a tool to run one, or document the wait. | 1.5 |
| 1.4 | `ServiceBusOptions` only has `ConnectionString`, and `ValidateOnStart` requires it. With managed identity the API needs the namespace and `DefaultAzureCredential` instead, and the validation must accept either. Until then the API cannot start in the cloud without a key-based connection string, which section 8 forbids. | 4.6 |
| 1.4 | The publish `TryTimeout` is 2 s (see `docs/code/StripeWebhooksController.md`). After the API scales from zero, the first send also opens the AMQP connection and authenticates, which may take longer against a cloud namespace and fail every attempt. Measure the first publish after a cold start and raise `TryTimeout` if needed. | 4.7 |
| M1 review | Section 8 says the ASP.NET Core rate limiter protects `POST /webhooks/stripe` from floods of unsigned requests, but no task builds it and the endpoint has none (30 unsigned requests in a row all got `400`, none `429`). Add a fixed-window limiter on the webhook route; Stripe resends on `429`, so a limit high enough for real traffic loses nothing. | 3.4 |
