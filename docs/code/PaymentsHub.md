# PaymentsHub

`src/Payments.Api/Hubs/PaymentsHub.cs`, with `HubConnectionCounter`, `PaymentsHubMethods` and `EventProcessedMessage` in the same folder, and the registration in `src/Payments.Api/Program.cs`.

The SignalR hub at `/hubs/payments` (section 7, task 5.3). The server pushes three messages to every connected client, and the client calls nothing: actions like reprocessing the dead-letter go through the REST API (section 7). This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Messages

| Message | Sent by | Payload |
| --- | --- | --- |
| `PaymentUpdated` | `PaymentNotificationsListener`, when the notification carries the payment | `PaymentSummary`, the same shape as an item of `GET /api/payments` |
| `EventProcessed` | `PaymentNotificationsListener`, for every notification | `EventProcessedMessage`: event ID and type, payment ID, outcome and trace ID |
| `QueueStats` | `QueueStatsBroadcaster`, every 5 s while a client is connected | `QueueStats`: active and dead-lettered messages of `payment-events`, and `CountLimit`, the most a count reaches (see `ServiceBusQueueStatsReader.md`) |

`PaymentUpdated` reuses the read API's shape, so the dashboard can put a pushed payment straight into the table it loaded over REST. The outcome in `EventProcessed` is `ProcessStripeEventOutcome` as it is (`Applied`, `IgnoredOutOfOrder`, `Redundant`, `Duplicate`): the worker only notifies those four (see "Notification" in `ProcessStripeEvent.md`). The trace ID (task 5.6) is the one the worker processed the event under, carried by the notification (see "Trace" in `ProcessStripeEvent.md`); it is `null` only when the worker runs without telemetry.

## Hub methods as constants, not a typed hub

A strongly typed hub (`Hub<IPaymentsClient>`) takes the message name from the interface method's name. `CLAUDE.md` requires the `Async` suffix on every method that returns `Task`, so the messages would go out as `PaymentUpdatedAsync`, which breaks the section 7 contract. `PaymentsHubMethods` holds the three names, and the senders call `Clients.All.SendAsync(PaymentsHubMethods.X, payload)`.

## Enums as strings

SignalR has its own JSON options, separate from the controllers'. Without `AddJsonProtocol` and `JsonStringEnumConverter`, `PaymentUpdated` would send `status` as a number while `GET /api/payments` sends `"Paid"` (found in task 5.1). Both now send the name, so the dashboard reads one format. `PaymentsHubTests.Payment_status_is_sent_as_a_string` checks it.

## Connection counter

`HubConnectionCounter` is a singleton that the hub increments in `OnConnectedAsync` and decrements in `OnDisconnectedAsync`. `QueueStatsBroadcaster` reads it to skip the Service Bus peeks while nobody is watching (section 7: "while a client is connected"). A hub instance lives for a single call, so the count can't be a field of the hub. Memory in the process is enough with one API replica (section 7). With several (M7) each replica counts its own clients, which is still what its own broadcaster needs.

## Public, like the read API

The hub has no authentication, like `GET /api/payments` (section 8). It reads nothing from the database, so an open connection doesn't keep the free database awake (the read API's open question in `docs/pending.md`). A connection does make the API peek `payment-events` every 5 s, but one broadcast serves every client, so the cost doesn't grow with the number of connections.

## Stateful reconnect stays off

CVE-2026-70159 (Microsoft.AspNetCore.SignalR.Common and .Core up to 10.0.12) lets a client using stateful reconnect block broadcasts. Stateful reconnect is opt-in on the server (`AllowStatefulReconnects` in `MapHub`), and this hub doesn't turn it on: the dashboard reloads over REST when it reconnects (section 7), so it doesn't need replayed messages. The test projects reference `Microsoft.AspNetCore.SignalR.Client` 10.0.13, because 10.0.12 fails the restore with NU1903 (warnings as errors). The API image uses the floating `aspnet:10.0` tag, which picks up the patched runtime at the next build.

## Tests

- `tests/Payments.Api.Tests/Hubs/PaymentsHubTests.cs`, with `WebApplicationFactory` and a fake `IQueueStatsReader`: a connected client receives `QueueStats`; with no client, the stats are not read; a failing read sends nothing and the connection stays up; `PaymentUpdated` sends the status as a string.
- `tests/Payments.IntegrationTests/Hubs/PaymentsHubTests.cs` is the task's done criterion: the real API (on the emulator) and the real worker process an event, and a client connected to the hub receives `PaymentUpdated`, `EventProcessed` and `QueueStats`. One message is dead-lettered before the client connects, and the test waits for a `QueueStats` that counts it, so stats from the wrong queue or a reader stuck at zero fail it (code review: the first version only checked the counts were not negative).

The clients connect over WebSockets through `TestServer.CreateWebSocketClient()`, the transport the dashboard uses, with `HttpMessageHandlerFactory` set for the negotiate request.
