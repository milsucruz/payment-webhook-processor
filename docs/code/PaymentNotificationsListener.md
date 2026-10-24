# PaymentNotificationsListener

`src/Payments.Api/Notifications/PaymentNotificationsListener.cs`, with the registration in `src/Payments.Api/Program.cs`.

The adapter between the `payment-notifications` queue and the API (task 5.2, section 4, step 10). The worker publishes a `PaymentNotification` after it commits an event (see "Notification" in `docs/code/ProcessStripeEvent.md`), and this `BackgroundService` receives it. Since task 5.3 it sends `PaymentUpdated` (only when the notification carries the payment) and `EventProcessed` to every client of the hub (`docs/code/PaymentsHub.md`), then logs the notification at Information: "Received notification for event {EventId} on payment {PaymentIntentId}: {Outcome}". The log from task 5.2 stays because it is how the cloud check in `docs/pending.md` sees a notification arrive without a dashboard open, and the volume is one line per event. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Processor options

Set in `Program.cs`, which registers a `ServiceBusClient` and the processor as keyed singletons (`PaymentNotificationsListener.ServiceKey`), and the listener receives the processor with `[FromKeyedServices]`. It doesn't depend on Infrastructure options (layering rule in `CLAUDE.md`, checked by `ControllerDependencyTests` for controllers and `BackgroundService` types), the same pattern as `PaymentEventsWorker`. The container created both singletons, so it disposes them on shutdown; the first version built the processor inside the hosted service's factory, where nothing disposed it (code review).

**Its own client.** The API's main `ServiceBusClient` has short retry options for the webhook (2 s per try, section 4: fail fast and let Stripe resend). A processor waits for messages for at most one try timeout, so on that client an idle listener asked for a message every 2 s, about 30 operations a minute while a replica is up, and a Service Bus outage spent the 2 retries at once and filled the log with errors. The listener's client uses the SDK's defaults (60 s per try), so an idle receive waits up to a minute (code review).

| Option | Value | Why |
| --- | --- | --- |
| `ReceiveMode` | `ReceiveAndDelete` | A notification is disposable (section 3): it expires in 60 s and the dashboard reloads over REST on reconnect. Peek-lock would add a lock, a settlement call per message and up to 5 deliveries of a notification that fails, and a failing one would end in the queue's dead-letter, where messages never expire. With `ReceiveAndDelete` a notification that fails is gone, which is the behavior section 4 already accepts for a lost notification |
| `MaxConcurrentCalls` | 1 | One operator and one API replica (section 7). The handler only sends to the hub, which is in memory. One call keeps the notifications in the order they were received |

## Shutdown: the container stops the processor

The listener doesn't override `StopAsync`. The processor is a singleton in the container, and its `DisposeAsync` stops processing (`CloseAsync`) and closes its link, so disposing the host is enough. Between the host's stop and that disposal the processor can still receive a notification and send it to a hub with no clients, which loses it, the same as any lost notification (section 3).

Task 5.2 had a `StopAsync` that called `processor.StopProcessingAsync`, and it broke the API tests in CI (task 5.3). With `WebApplicationFactory` and minimal hosting, the app's own `app.Run()` starts the host on another thread. When a test disposes the factory, the factory calls `StopAsync`, `app.Run()` wakes up on `ApplicationStopping` and disposes the container, and the processor got disposed in the middle of `StopProcessingAsync`: `ObjectDisposedException` on its internal `SemaphoreSlim`. On Linux, where `sb://localhost` refuses at once, it happened in almost every run. On `main` it only showed up as xUnit "Test Class Cleanup Failure", which doesn't fail the run. The task 5.3 hub tests dispose their factory inside the test, so it became a failed test. In production the stop and the disposal run one after the other, so the race only existed in the test harness, but the override had no other use.

## No `try`/`catch`

As in the worker, the handler catches nothing. An unreadable body makes `PaymentNotificationJson.Deserialize` throw, and the SDK calls `ProcessErrorAsync`, which logs it as an error. In `ReceiveAndDelete` the message is already gone, so a broken notification is logged once and never comes back.

The hub sends run inside the same handler, so a send that fails also loses the notification (code review). `Clients.All.SendAsync` doesn't throw when one client's connection fails (the lifetime manager logs it and goes on), so in practice the sends fail only when the processor is stopping and cancels `args.CancellationToken`. A notification lost at shutdown is the case section 3 already accepts, and the dashboard recovers with the REST reload on reconnect (section 7). If `PaymentUpdated` fails, `EventProcessed` is skipped too, for the same reasons.

## The API at zero replicas

The listener only runs while a replica is up. Container Apps scales the API by HTTP traffic, and an open AMQP link doesn't count, so the listener never keeps a replica alive. With the API at zero, notifications expire in the queue after 60 s, by design (section 3: "if nobody has the dashboard open, the API is at zero replicas and the notification simply expires"). Whether an open SignalR connection keeps the replica up is task 5.8.

## Tests

`tests/Payments.IntegrationTests/Worker/PaymentNotificationsTests.cs`, in `WorkerFixture`'s collection, so it reuses the emulator and the database:

- `Processed_event_publishes_a_notification_that_expires_in_60_seconds` runs the real worker and reads the notification from the queue: the fields, and `ExpiresAt - EnqueuedTime` of 60 s, which comes from the queue's `DefaultMessageTimeToLive` (the emulator config and `servicebus.bicep`, task 4.2). The publisher sets no time to live of its own, so the queue is the only place to change it.
- `tests/Payments.IntegrationTests/Hubs/PaymentsHubTests.cs` runs the real API, so this listener, on the emulator: a client connected to the hub receives `PaymentUpdated` and `EventProcessed` for an event the real worker processed. It replaced task 5.2's `Processed_event_produces_a_notification_that_the_api_receives`, which built the listener by hand and checked only its log: the hub test covers the same path and goes one step further.

The integration project references `Payments.Api` with `Aliases="api"` (`extern alias api;` in the test). The API and the worker both have a global `Program` class, and without the alias `WorkerFixture`'s `Program.AddPaymentEventsWorker` would be ambiguous.

The API tests (`WebApplicationFactory`) start the listener too, against a Service Bus address that doesn't exist. The processor only logs connection errors, so the tests are unaffected.
