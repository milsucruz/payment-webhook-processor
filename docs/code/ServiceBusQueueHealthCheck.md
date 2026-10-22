# ServiceBusQueueHealthCheck

`src/Payments.Infrastructure/Messaging/ServiceBusQueueHealthCheck.cs`, registered in `src/Payments.Api/Program.cs`

The check behind `GET /health/ready` (architecture section 9: "Connection to Service Bus"). Container Apps calls that endpoint as the API's readiness probe (task 4.7) and sends traffic to the replica only while it answers `200`.

## What it does

It opens a receiver on `payment-events` with the API's singleton `ServiceBusClient` and peeks one message. A peek reads without locking, so the message stays where it was, its `DeliveryCount` does not change, and an empty queue just returns `null`. Any answer means the namespace is reachable, the identity got a token, and the AMQP connection is open. The integration test checks the "stays where it was" part: after the check, the message is received with `DeliveryCount` 1.

The check does not catch exceptions. `HealthCheckService` already turns a thrown exception into the registration's failure status (`Unhealthy`) and logs it, so a `try`/`catch` here would only repeat that.

## Why a peek

| Alternative | Why not |
| --- | --- |
| `ServiceBusAdministrationClient.GetQueueRuntimePropertiesAsync` (also what the Xabaril `AspNetCore.HealthChecks.AzureServiceBus` package does by default) | Needs the Manage claim, which is Azure Service Bus Data Owner in RBAC. The API has Data Sender and Data Receiver only (section 8) |
| Sending a test message | Writes to the queue the worker reads, and the worker would process it |
| Only creating the client | `ServiceBusClient` opens no connection until the first operation, so it would prove nothing |

The peek fits the Data Receiver role the API already has on `payment-events` for the dead-letter screen.

## Why on the client the publisher uses

The receiver shares the AMQP connection of the singleton `ServiceBusClient`. When the API starts from zero replicas, the readiness probe opens that connection and gets the token before the first webhook is routed to the replica, instead of the first publish doing it inside Stripe's request (pending item of task 1.4). The receiver is disposed after each check; the connection stays.

The client's retry options (2 retries, 2 s `TryTimeout`) apply to the peek too, so an unreachable namespace takes up to about 8 s to fail. The probe's `timeoutSeconds` is 10 for that reason (see `docs/azure/container-apps.md`).

## Live and ready

`/health/live` maps with `Predicate = _ => false`, so it runs no check and answers as long as the process responds. If it ran this check, a Service Bus outage would fail the liveness probe and Container Apps would restart a healthy API in a loop. Readiness is the right reaction: the replica stops receiving traffic until the namespace answers again.

The database is not checked, by design (section 9): it may be paused, and the API has no database connection anyway.

## Tests

- `tests/Payments.Api.Tests/Health/HealthEndpointsTests.cs`: with a connection string to an unreachable namespace, `/health/live` answers `200 Healthy` and `/health/ready` answers `503 Unhealthy`. Removing the `Predicate` from `/health/live` makes the first test fail.
- `tests/Payments.IntegrationTests/Messaging/ServiceBusQueueHealthCheckTests.cs`: against the emulator, the check is `Healthy` and the peeked message is still in the queue with `DeliveryCount` 1 when received.
