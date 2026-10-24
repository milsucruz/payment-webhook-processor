# ServiceBusQueueStatsReader

`src/Payments.Infrastructure/Messaging/ServiceBusQueueStatsReader.cs`, registered in `src/Payments.Api/Program.cs` with the API's main `ServiceBusClient` and `ServiceBus:EventsQueueName`.

Implements `IQueueStatsReader` (task 5.3): counts the active messages of `payment-events` and of its dead-letter subqueue. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Counting by peek, not with the administration client

The direct way to get the counts is `ServiceBusAdministrationClient.GetQueueRuntimePropertiesAsync`, which returns `ActiveMessageCount` and `DeadLetterMessageCount`. It needs the *Manage* right, which in RBAC means Azure Service Bus Data Owner. Section 8 gives the API only Data Sender and Data Receiver on `payment-events`, and the pipeline's conditioned Role Based Access Control Administrator can only assign those two and Key Vault Secrets User. Data Owner would change a section 8 decision and let the API delete the queue. Reading the counts from ARM (`Microsoft.ServiceBus/namespaces/queues` with Reader) has the same problem: a role the condition doesn't allow.

A peek needs only Data Receiver, which the API already has for the readiness check and the dead-letter screen. It walks the queue in batches of 250 and counts what comes back, so it works the same in the emulator and in Azure, with no new role.

| | Administration client | Peek |
| --- | --- | --- |
| Role | Data Owner (Manage) | Data Receiver, already granted |
| Cost per read | One call | One call per 250 messages, plus the bodies |
| Exact count | Any size | Up to `MaxCountedMessages` (1000) |

## The 1000 cap

A peek transfers the message bodies (a Stripe event is a few KB). With a backlog of 100,000 messages a single read would download hundreds of MB every 5 s, so counting stops at 1000. A count of 1000 means "1000 or more", and the dashboard (task 5.5) should show it that way. In this project's demos the backlog is the `flood` mode's 100 messages, well under the cap.

## What "active" means here

A peek returns every message still in the queue, including the ones a worker has locked and is processing. That matches `ActiveMessageCount`, which also counts locked messages. The project doesn't use scheduled or deferred messages, which a peek would also return.

## A new receiver per read

Each count opens a receiver and disposes it. A receiver's peek cursor moves forward with each call, so a reused receiver would start the next count where the last one stopped. A new receiver starts from the head, and opening the link costs little next to a 5 s interval.

## Tests

`tests/Payments.IntegrationTests/Messaging/ServiceBusQueueStatsReaderTests.cs`, in `WorkerFixture`'s collection, so it reuses that emulator instead of starting another container: four messages sent and one dead-lettered count as 3 and 1, and an empty queue counts 0 and 0. The 1000 cap is not tested: filling the emulator with 1000 messages would add minutes to check a `Math.Min`.
