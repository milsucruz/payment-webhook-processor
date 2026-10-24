# PaymentsTelemetry

`src/Payments.Infrastructure/Telemetry/PaymentsTelemetry.cs` and `TraceNoiseFilter.cs`

Sets up traces and logs for the API and the worker (task 5.6, section 9). Both `Program.cs` files call `PaymentsTelemetry.Add(builder, serviceName, ...)` as their first line; the API passes the ASP.NET Core instrumentation, which needs the web framework that Infrastructure doesn't reference.

## What it registers

| Piece | Why |
| --- | --- |
| `Azure.Experimental.EnableActivitySource` switch | The Service Bus SDK only creates spans with it on |
| Resource `service.name` (`payments-api`, `payments-worker`) | Becomes the role name in Application Insights, which tells the two containers apart |
| Sources `Payments.Processing` and `Azure.*` | The transaction span from `ProcessStripeEvent` and every Azure SDK span (send, process, complete, token requests) |
| HttpClient and SqlClient instrumentation | Outgoing HTTP calls and one span per SQL command, under the transaction span |
| `TraceNoiseFilter` | Drops the polling spans and the spans under a dropped parent, see below |
| Azure Monitor trace and log exporters | Only when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set |

## The switch is read once

Azure.Core reads `Azure.Experimental.EnableActivitySource` the first time any Azure client creates a span and keeps the value. Setting it later changes nothing, so it is the first thing `Add` does, before any `ServiceBusClient` exists. The integration tests found this: the test fixture creates its own Service Bus client before the API starts, so `tests/Payments.IntegrationTests/Telemetry/ServiceBusTracingSwitch.cs` turns the switch on in a module initializer. Setting the `AZURE_EXPERIMENTAL_ENABLE_ACTIVITY_SOURCE` environment variable would also work, but it would have to be repeated in compose, the Bicep and the tests.

## Six spans, one trace

The API's request span is the parent of the send to `payment-events`. The SDK writes the message's own span context into the message (`Diagnostic-Id`), and the processor makes `ServiceBusProcessor.ProcessMessage` a child of it, so the worker's span joins the same trace. Inside it, the transaction span (with the SQL commands under it), the send to `payment-notifications` and the completion; the API's `ProcessMessage` for the notification is again a child of that message. A real run (`EventTraceTests`):

```
[Microsoft.AspNetCore] Server POST webhooks/stripe
  [Azure.Messaging.ServiceBus.Message] Producer Message (payment-events)
    [...ServiceBusProcessor] Consumer ServiceBusProcessor.ProcessMessage (payment-events)       ← worker
      [Payments.Processing] Internal payment transaction
        [OpenTelemetry.Instrumentation.SqlClient] Client SELECT / INSERT ...
      [Azure.Messaging.ServiceBus.Message] Producer Message (payment-notifications)
        [...ServiceBusProcessor] Consumer ServiceBusProcessor.ProcessMessage (payment-notifications)  ← API, hub send
      [...ServiceBusSender] Client ServiceBusSender.Send (payment-notifications)
      [...ServiceBusReceiver] Client ServiceBusReceiver.Complete
  [...ServiceBusSender] Client ServiceBusSender.Send (payment-events)
```

The hub send has no span of its own: SignalR doesn't create one, and it is the only work in the API's processing span.

## Dropping the noise

Every receiver call is a span. The two processors poll their queues, the readiness check peeks `payment-events` every 5 s, and `QueueStats` peeks twice every 5 s while a dashboard is open. Each of those is the root of a trace with nothing else in it, and together they would be most of the ingestion against the 0.2 GB daily cap. `TraceNoiseFilter` clears the `Recorded` flag of a span from the `ServiceBusReceiver` source that has no parent, and the export processors skip spans that are not recorded. It also clears it for any span whose in-process parent is not recorded, which is what a parent-based sampler would do: the Azure Monitor sampler decides by trace ID only, so without this rule the readiness check's peek, a child of the filtered `/health` request, was exported every 5 s (found by the code review; `EventTraceTests` calls `/health/ready` with a sampler that, like Azure Monitor's, ignores the parent, and fails without the rule). A receiver span inside a request or a message's processing (completing, dead-lettering, the dead-letter reprocess) has a parent and is kept. Removing the filter makes `EventTraceTests` fail.

It is a processor and not a sampler because the Azure Monitor exporter sets its own sampler (`SamplingRatio`, `TracesPerSecond`), which would replace one set here depending on the registration order. A processor runs whatever the sampler is. The exporter is set to `SamplingRatio = 1` and no rate limit: the volume is a few events per minute, and a per-process rate limit could keep the API's half of a trace and drop the worker's.

The `/health` requests are left out with the ASP.NET Core instrumentation's `Filter`, which clears the request span's `Recorded` flag; the second rule then drops its peek.

## Logs: `ILogger` with two providers

`ILogger` stays the logging API; Serilog is only the console output. `ClearProviders` removes the default console provider, then two providers are added:

- `SerilogLoggerProvider`, writing `RenderedCompactJsonFormatter` JSON: `@t`, `@m`, `@l`, `@tr` (trace ID), `@sp` (span ID) and the properties, one line per entry.
- The OpenTelemetry provider (from `WithLogging`), with scopes and the formatted message, exported to Application Insights as traces linked to the operation.

The provider is added with `AddProvider(new SerilogLoggerProvider(...))`, not Serilog's `AddSerilog`: `AddSerilog` also adds a `Trace` filter rule for its provider, which beats the `Logging:LogLevel` rules and printed every Kestrel `Debug` line. With `AddProvider` the same levels from `appsettings.json` apply to the console and to Application Insights. Serilog's `writeToProviders`, the other usual setup, would make Serilog the hub and pass its own converted event on to the OpenTelemetry provider; with two `ILogger` providers each one gets the original structured state and scopes, and there is no conversion to depend on.

`ActivityTrackingOptions` is turned off: by default the host adds `TraceId`, `SpanId` and `ParentId` to every scope, which repeats what `@tr`/`@sp` and the OpenTelemetry log record already carry.

## Event ID on every line

The worker opens a scope `EventId = message ID` around each delivery (`PaymentEventsWorker`), so every line of that delivery, including EF Core's and a parse failure's, carries it. In the API, `ReceiveStripeEvent` logs "Published Stripe event {EventId}" with the request's trace ID, which links the webhook to the worker's lines for the same event. Lines from ASP.NET Core with their own MEL event ID (such as EF's) show that ID in the console's `EventId` property instead, since the entry's own properties win over the scope.

## Authentication to Application Insights

Application Insights has `DisableLocalAuth`, so the ingestion endpoint refuses the instrumentation key alone. The exporter gets `DefaultAzureCredential`, which picks the container's identity through `AZURE_CLIENT_ID` (as in `ServiceBusClientFactory.md`), and both identities have Monitoring Metrics Publisher on the Application Insights resource (`infra/monitoring.bicep`). The connection string still carries the instrumentation key, but only to identify the resource, so the Bicep passes it as a plain environment variable.

## Duplicate noise

`Program.AddPaymentEventsWorker` logs EF Core's `CommandError` (20102) and `SaveChangesFailed` (10000) at `Debug`. A duplicate delivery fails the inbox insert on purpose; `PaymentStore` turns that into `DuplicateEvent` and the use case logs "already processed", so EF's two `Error` entries with the full stack were noise that would count against the alerts and the cap. Any other failure still reaches the use case, which logs its outcome (`TransientFailure` keeps the exception).

## Tests

- `EventTraceTests` (integration): the real API (`WebApplicationFactory`, with an in-memory exporter added) and worker on the emulator; a signed webhook produces one trace with the request, the send to `payment-events`, the worker's processing, the transaction span with SQL commands under it, the send to `payment-notifications` and the API's processing of the notification; after a `/health/ready` call, the only exported receiver span is the completion; the inbox row has the trace ID. API and worker run in one process, so the API's tracer provider also records the worker's spans: the test checks the chain and the propagation, and the cloud check (each container exporting its half) is in `docs/tasks.md`.
- `ProcessStripeEventTests`: the transaction span's parent, name and tags, and the trace ID in the inbox and the notification. An injected fault leaves a failed span for its attempt before the retry's.

## Kept after the code review

- **The scope key is `EventId`**, the same name every log template in the use cases uses, so one key finds the API's and the worker's lines for an event. A line with its own MEL event ID inside a delivery shows that ID instead; with EF Core at `Warning`, that is rare.
- **EF Core's command errors at `Debug` in the whole worker**, not only for the duplicate. Every other failure still reaches a log with its exception: the final `TransientFailure`, or `ProcessErrorAsync` for anything the use case doesn't catch.
- **No stack on the retry, rollback, queue stats and readiness logs.** That was the point of the `docs/pending.md` item; the message stays, and the last failure of a delivery keeps the exception.
