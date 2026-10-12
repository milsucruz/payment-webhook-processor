# Pending items

Open questions found during a task that are out of its scope. Review this file at the end of every milestone: resolve each item, move it to a task, or drop it with a reason.

| Found in | Item | Resolve by |
| --- | --- | --- |
| 1.1 | The webhook body limit is 256 KB (section 8), but the Service Bus Basic 256 KB cap counts the body **and** the message headers. A body just under 256 KB passes the API and then fails to publish, so the API answers `500` and Stripe retries until it gives up. Decide whether to lower the limit (and fix section 8) or accept and document the edge case. Real `payment_intent` events are a few KB. | 1.4 |
| 1.3 | `IEventPublisher.PublishAsync` takes the event ID ready-made, so something has to read `id` from the raw JSON after the signature check. Decide where that happens and what the API answers when the body is not JSON or its `id` is missing, empty or longer than 128 characters. The `MessageId` setter throws `ArgumentException` for those values, and the 1.4 rule "any publish failure answers `500`" would make Stripe resend an event that can never succeed, so it should be `400`. | 1.4 |
| 1.3 | The Service Bus SDK retries by default (3 retries, 60 s `TryTimeout` per attempt), so a failing publish can hold the webhook request far longer than Stripe waits for a response. Set `ServiceBusClientOptions.RetryOptions` where the client is created so the total stays under Stripe's webhook timeout. | 1.4 |
| 1.3 | `ServiceBusEventPublisher` takes the queue name as a plain `string` and owns one sender link. Register it as a singleton through a factory, not scoped, or every webhook opens and closes an AMQP link. | 1.4 |
| 1.3 | The `Payments.IntegrationTests` project is kept out of CI by a `FullyQualifiedName` filter in `ci.yml`. Remove the filter when integration tests join CI. | 3.5 |
| 1.3 | `ServiceBusEventPublisherTests` starts its own emulator and SQL Server per test through `IAsyncLifetime`. With one test that is fine; when more integration tests arrive, share the containers through a class or collection fixture. | 3.5 |
