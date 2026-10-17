# PaymentEventsWorker

`src/Payments.Worker/PaymentEventsWorker.cs`, with the registrations in `src/Payments.Worker/Program.cs`.

The adapter between the `payment-events` queue and `ProcessStripeEvent` (see `docs/code/ProcessStripeEvent.md`). It receives a message, calls the use case and settles the message from the result. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## What it does per message

1. Opens a DI scope. `ProcessStripeEvent`, `PaymentStore` and `PaymentsDbContext` are scoped, so each message gets its own context and its own transaction. A `DbContext` is not thread-safe, and the processor runs four handlers at once.
2. Calls `ExecuteAsync` with the body as a string and the processor's cancellation token. The worker never reads the event itself (layering rule in `CLAUDE.md`).
3. Settles the message from the result:

| Result | Settlement | Why |
| --- | --- | --- |
| `Applied`, `IgnoredOutOfOrder`, `Redundant` | Complete | The transaction is committed, so the message can leave the queue (step 8 of section 4) |
| `Duplicate` | Complete | The event was already processed; nothing was written (section 6) |
| `InvalidEvent`, `Conflict` | Abandon | Provisional: see below |

`CompleteMessageAsync` only runs after `ExecuteAsync` returns, and `ExecuteAsync` only returns those outcomes after the commit. If the worker dies between the two, the message comes back and the inbox turns it into a `Duplicate`.

## Permanent errors are abandoned until task 3.2

Section 6 sends permanent errors straight to the dead-letter, with reason and description. That is task 3.2. Until then the worker abandons them. The message comes back, fails the same way, and after the fifth delivery (`MaxDeliveryCount`) Service Bus moves it to the dead-letter by itself. Nothing is lost and nothing is completed by mistake. The cost is four useless deliveries and a dead-letter reason of `MaxDeliveryCountExceeded` instead of the parser message. Task 3.2 replaces the `AbandonMessageAsync` with `DeadLetterMessageAsync` using `result.Reason`.

## No `try`/`catch`: exceptions go back to the queue

The handler doesn't catch anything (layering rule in `CLAUDE.md`). When the use case throws (database down, a concurrency conflict until task 2.6, cancellation on shutdown), the exception leaves the handler and the SDK:

1. calls `ProcessErrorAsync`, which logs it as an error with the queue name and the error source;
2. abandons the message, because it isn't settled and the receive mode is peek-lock.

The second point was checked in the task 2.5 session, not taken from the docs. With `AutoCompleteMessages = false`, a handler that threw had its message delivered again about 2 s later, while the lock is 60 s. If the SDK only let the lock expire, the redelivery would take the full minute.

`ProcessErrorAsync` also receives errors that aren't tied to a message, such as a connection failure while the emulator is still starting. The processor keeps retrying on its own, so the worker can start before the emulator is ready.

## Processor options

Set in `Program.cs`, which builds the processor from the singleton `ServiceBusClient` and the queue name in `ServiceBusOptions`. The worker receives the ready processor, so it doesn't depend on Infrastructure options.

| Option | Value | Why |
| --- | --- | --- |
| `ReceiveMode` | `PeekLock` | The message stays in the queue, invisible, until it is settled (step 5 of section 4) |
| `AutoCompleteMessages` | `false` | The default is `true`, which would complete even when the use case asks for an abandon. Section 6 and the rule in `CLAUDE.md` |
| `MaxConcurrentCalls` | 4 | Section 6: limits pressure on the free database |
| `PrefetchCount` | 0 | A prefetched message's lock starts ticking before a handler takes it. Messages take milliseconds, so prefetch gains nothing |
| `MaxAutoLockRenewalDuration` | SDK default, 5 min | Renews the 60 s lock while a handler runs. Only matters for a slow message, such as the `slow` fault in task 3.3 |

The `ServiceBusClient` keeps the SDK's default retry. The API caps it at about 8 s because Stripe waits on the response (see `docs/code/StripeWebhooksController.md`). Nobody waits on the worker.

## Start and stop

`ExecuteAsync` subscribes the two handlers and starts the processor, then returns. The processor runs on its own tasks. `StopAsync` stops the processor before the host stops, so running handlers finish or see their token cancelled. A handler cancelled before its commit throws and the message comes back. A handler cancelled after its commit fails to complete, and on redelivery the message is a `Duplicate`.

The container disposes the processor and the client, which are singletons, when the host shuts down.

## Configuration

| Key | Locally | Cloud |
| --- | --- | --- |
| `ServiceBus:ConnectionString` | Emulator key: `appsettings.Development.json` for `dotnet run`, environment variable in compose | Managed identity, task 4.6 (`docs/pending.md`) |
| `ServiceBus:EventsQueueName` | `appsettings.Development.json` | Container App setting |
| `ConnectionStrings:Payments` | `dotnet user-secrets` for `dotnet run`, environment variable in compose | Passwordless (section 8), task 4.6 |

`ServiceBusOptions` is validated on start, as in the API. The connection string is checked in `Program.cs` before `AddDbContext`, so a missing value stops the worker at startup instead of at the first message.

`appsettings.Development.json` lowers `Microsoft.EntityFrameworkCore` to `Warning`. At `Information`, EF logs every SQL command, three or four per message, and buries the use case's one line per event. Errors from EF still show up.

In compose the worker waits for `migrator` to finish successfully and for `servicebus` to start, so it never meets a database without the schema.

## Tests

`tests/Payments.Worker.Tests/WorkerDependencyTests.cs` checks that no type in the worker, apart from `Program`, takes or holds a `Payments.Infrastructure` type. It is the same check as `ControllerDependencyTests` in the API. Adding a `PaymentStore` constructor parameter to the worker makes it fail.

There is no unit test of the settlement mapping: building fake `ProcessMessageEventArgs` costs more than the `switch` it would check. Integration tests with the emulator arrive in task 3.5.

## Verified in task 2.5

`stripe trigger payment_intent.succeeded` sends `payment_intent.succeeded` and `payment_intent.created` in the same second, for a payment the database doesn't know yet. Both handlers ran at once:

- `succeeded` created the payment as paid.
- `created` read no payment too, tried to insert it, and failed with a `PK_Payments` violation in `CommitAsync`. That is the concurrent-creation case of task 2.6. The exception went back to the queue, and on redelivery `created` found the paid payment and was recorded as out of order.

The final state is correct: one payment, two inbox rows, two history rows. The history IDs are 1 and 3, because the rolled-back insert used identity 2. Until task 2.6, this race logs a full stack trace as an error; 2.6 turns it into a retry inside the use case.

`stripe events resend` of the `succeeded` event was logged as `already processed`, and the row counts didn't change. A throwaway script peeked the queue and its dead-letter after each step and found both empty.
