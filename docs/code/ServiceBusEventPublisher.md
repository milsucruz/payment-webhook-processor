# ServiceBusEventPublisher

`src/Payments.Infrastructure/Messaging/ServiceBusEventPublisher.cs`

Publishes a received Stripe event to the `payment-events` queue. It implements the `IEventPublisher` port from Application. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Where it lives

The port `IEventPublisher` is in `Payments.Application/Messaging`, as task 1.3 asks. The API depends on the port, not on Azure types, so task 1.4 can test the controller with a failing publisher instead of a real queue. The Service Bus implementation is in Infrastructure, the only project that references Azure packages besides the executables (architecture section 11).

## The message

Architecture section 4, step 3:

| Property | Value | Why |
| --- | --- | --- |
| `MessageId` | The event ID (`evt_...`) | The idempotency key travels with the message. Basic tier has no duplicate detection, so this does not deduplicate on the broker; the worker's inbox does (section 6). It makes a message traceable to its Stripe event. |
| Body | The original JSON, unchanged | The worker parses the event Stripe sent, not a re-serialized copy that could drop fields. |
| `ContentType` | `application/json` | Helps anyone inspecting the queue or the dead-letter. |

The body is a `string` because that is what the controller reads and what the signature verifier takes. `ServiceBusMessage(string)` encodes it as UTF-8, which is the encoding Stripe sends, so the bytes on the queue match the request body.

The publisher does not read the JSON. It takes the event ID as a parameter, so it stays a transport that knows nothing about Stripe's format. The controller reads `id` from the body after the signature check (see `docs/code/StripeWebhooksController.md`).

## One sender, reused

The constructor creates one `ServiceBusSender` and every call reuses it. A sender opens an AMQP link, which is expensive, and the SDK documents it as thread-safe, so one per publisher is the intended use. The class implements `IAsyncDisposable` to close that link. The `ServiceBusClient` is passed in and not owned: whoever creates it (the DI container) disposes it. The class is `sealed` because it owns that link: a subclass that skipped `DisposeAsync` would leak it.

The API's `Program.cs` registers both the `ServiceBusClient` and the publisher as singletons, the publisher through a factory because its constructor takes the queue name as a plain `string` (from `ServiceBusOptions.EventsQueueName`). Registered as scoped, every webhook would open and close an AMQP link. The container disposes both at shutdown.

## Errors

Nothing is caught. A failed send raises `ServiceBusException` (for example `MessageSizeExceeded`, or a timeout), and the controller turns any exception into `500` so Stripe resends (section 4, failure table).

The SDK retries before raising. Those options belong to the `ServiceBusClient`, so they are set where the API creates it; the values and why are in `docs/code/StripeWebhooksController.md`.

## Tests

`tests/Payments.IntegrationTests/Messaging`. The test starts the official emulator with Testcontainers (`Testcontainers.ServiceBus`, which also starts the SQL Server the emulator needs), with the same `infra/servicebus-emulator/config.json` as compose, so the queue in the test is configured like the local one. It publishes one event, receives it, and checks `MessageId`, body and `ContentType`.

The emulator image tag (`2.0.1`) is written in both the test and `docker-compose.yml`. Change both together; reading the compose file from the test would cost more than the duplication.

`ci.yml` filters out the whole `Payments.IntegrationTests` project by name until task 3.5. Filtering by project, not by a trait on each class, means a new integration test cannot forget the marker and break CI. Run them locally with Docker:

```bash
dotnet test tests/Payments.IntegrationTests
```

The first run pulls the emulator and SQL Server images and takes a few minutes; later runs take under a minute.
