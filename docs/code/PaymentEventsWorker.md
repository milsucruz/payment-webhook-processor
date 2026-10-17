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
| `InvalidEvent` | Dead-letter, reason `InvalidEvent` | Permanent error: the body is unreadable or the type is unsupported, so every delivery would fail the same way (section 6). See below |
| `Conflict` | Dead-letter, reason `ImpossibleConflict` | Permanent error: the event contradicts a recorded fact, and no retry changes that (section 6). See below |
| `ConcurrencyConflict` | Abandon, with `AbandonReason` | The use case already retried from the read `ProcessStripeEvent.MaxConcurrencyAttempts` times (task 2.6). Losing that many races in a row is unlikely but not a broken assumption, so the message goes back for a later delivery |
| `TransientFailure` | Abandon, with `AbandonReason` | The use case already made 1 attempt plus 3 retries over about 6 s (task 3.1, `docs/code/ProcessStripeEvent.md`). The database is still unreachable, so the message goes back and Service Bus delivers it again, up to 5 times. Not a permanent error |

`CompleteMessageAsync` only runs after `ExecuteAsync` returns, and `ExecuteAsync` only returns those outcomes after the commit. If the worker dies between the two, the message comes back and the inbox turns it into a `Duplicate`.

## Permanent errors go straight to the dead-letter

Section 6 sends permanent errors to the dead-letter on the first delivery, with reason and description (task 3.2). Service Bus gives a dead-lettered message two properties, and the worker fills them differently:

| Property | Value | Why |
| --- | --- | --- |
| `DeadLetterReason` | A fixed code per outcome: `InvalidEvent` or `ImpossibleConflict` (private constants on the class; the test asserts the literal, because the code is a contract that the dead-letter screen and operators rely on, and comparing against the constant would let a changed value pass) | A short, stable value that the dead-letter screen (task 3.4) can show and filter. It follows what Service Bus itself writes when it dead-letters a message: a code such as `MaxDeliveryCountExceeded` or `TTLExpiredException` in the reason, and the detail in the description |
| `DeadLetterErrorDescription` | `result.Reason`, the use case's text: the parser message ("The event body is not valid JSON.") or the conflict ("Event evt_... requests Paid for payment pi_..., which is Canceled.") | The operator needs to know which field was wrong or which fact was contradicted. Only the use case knows that, since the worker never reads the event |

Why these choices:

- **The code comes from the worker, the text from the use case.** Choosing the settlement, and what to write on it, is the adapter's job (layering rule in `CLAUDE.md`). The code depends only on the outcome, so the worker can pick it without reading the event. The text depends on the event, so the use case writes it and logs it (warning) before returning.
- **`ImpossibleConflict`, not the enum name.** `Conflict.ToString()` would read like `ConcurrencyConflict`, which never goes to the dead-letter on its own. The glossary term (section 1) is "impossible conflict".
- **Not a fixed text for every outcome.** It would hide which field of the body was wrong (see "Result record" in `docs/code/ProcessStripeEvent.md`).
- **No extra log in the worker.** The use case already logged the reason as a warning; a second line would say the same thing.

The SDK refuses a reason or description longer than 4096 characters with `ArgumentOutOfRangeException`. The worker doesn't truncate. The codes are short, and the description is built from fixed text plus Stripe's event ID, payment ID and type, which are short. If a description ever went over the limit, the exception would leave the handler, the message would be abandoned (section below) and, after 5 deliveries, Service Bus would dead-letter it with `MaxDeliveryCountExceeded`. Nothing is lost, only the reason gets worse.

Dead-lettering keeps the message as it was: same `MessageId` (the `evt_` ID) and same body. That is what the reprocess endpoint of task 3.4 republishes.

The other way to reach the dead-letter is `MaxDeliveryCount`: a message abandoned 5 times (transient failure that never ends, concurrency conflicts, or an exception) is moved by Service Bus with the reason `MaxDeliveryCountExceeded`. Until task 3.2 permanent errors took that path too, costing four useless deliveries and losing the parser message.

## The reason travels with an abandon

On `TransientFailure` and `ConcurrencyConflict` the worker abandons with one application property, `AbandonReason`, set to `result.Reason` (task 3.4). Service Bus keeps application properties changed on abandon, so when the fifth abandon makes it dead-letter the message as `MaxDeliveryCountExceeded`, the last reason is still on it, and the dead-letter list shows it (see `docs/code/DeadLettersController.md`). Without it the screen couldn't tell a database outage from a poison message, since both arrive with the same Service Bus reason.

- **Only these two outcomes.** They are the only abandons the worker chooses. A thrown exception (a poison message) is abandoned by the SDK, with no properties, so a dead-lettered message without `AbandonReason` was never abandoned for the database.
- **Prefixed with the delivery number**: `Delivery 2: Event evt_... for payment pi_... failed with a transient error on all 4 attempts: ...`. A property set on abandon stays on the message, and the SDK's abandon after an exception doesn't clear it. So a message that hit the database once and then threw on the remaining deliveries reaches the dead-letter with an old reason. The code review of task 3.4 found this; the number shows it: `Delivery 1:` on a message dead-lettered after 5 deliveries means deliveries 2 to 5 failed some other way. Clearing the property on every delivery would need a write to the broker before processing, for a case the number already explains.
- **The rest of the text is the use case's**, the same one it logged as a warning. The worker still doesn't read the event.
- **The property name is a contract.** The worker writes the literal `AbandonReason` and `ServiceBusDeadLetterQueue` reads it, each from its own private constant, because the worker can't reference Infrastructure. Both tests assert the literal, the same way the dead-letter codes are checked.
- **No reason, no properties.** `Reason` is nullable on the result; if it is null the worker passes `null`, which abandons without changes.

Reprocessing removes the property from the copy it sends back, so a stale reason doesn't follow the message (see `docs/code/ServiceBusDeadLetterQueue.md`).

`PaymentEventsWorkerDeadLetterTests` checks it with the emulator: a fault injector that always throws `TimeoutException` makes every attempt transient, and after about 6 s the peeked message carries `AbandonReason` with `Delivery 1:` and the use case's text.

## No `try`/`catch`: exceptions go back to the queue

The handler doesn't catch anything (layering rule in `CLAUDE.md`). When the use case throws (an error that is not transient, or cancellation on shutdown; transient database errors are retried inside the use case since task 3.1), the exception leaves the handler and the SDK:

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

There is no unit test of the settlement mapping: building fake `ProcessMessageEventArgs` costs more than the `switch` it would check. It is checked against the real Service Bus emulator instead.

`tests/Payments.IntegrationTests/Worker/PaymentEventsWorkerDeadLetterTests.cs` is task 3.2's done criterion. It starts the emulator in Testcontainers, sends a message whose body is `not json`, runs the real `PaymentEventsWorker` with a processor built like the one in `Program.cs` (peek-lock, no auto-complete), and receives from the dead-letter subqueue. It asserts the same `MessageId` and body, the reason `InvalidEvent`, the parser message as description, and `DeliveryCount` 1, which proves the message went on the first delivery and not after five. The services are registered as in `Program.cs`, but the database connection string points nowhere: an unreadable event fails in the parser, before the use case opens a transaction, so no SQL Server is needed. Putting the `InvalidEvent` case back on the abandon path fails the test with `MaxDeliveryCountExceeded` as the reason (mutation check in the task 3.2 session).

The impossible conflict takes the same path with the other code. Its integration test ("paid" for a canceled payment, dead-lettered on the first delivery) belongs to task 3.5, together with the poison message, the duplicate delivery and the crash after commit.

## Verified in task 2.5

`stripe trigger payment_intent.succeeded` sends `payment_intent.succeeded` and `payment_intent.created` in the same second, for a payment the database doesn't know yet. Both handlers ran at once:

- `succeeded` created the payment as paid.
- `created` read no payment too, tried to insert it, and failed with a `PK_Payments` violation in `CommitAsync`. That is the concurrent-creation case of task 2.6. The exception went back to the queue, and on redelivery `created` found the paid payment and was recorded as out of order.

The final state is correct: one payment, two inbox rows, two history rows. The history IDs are 1 and 3, because the rolled-back insert used identity 2. At that point this race logged a full stack trace as an error. Since task 2.6 the use case retries it from the read inside the same delivery (see `docs/code/ProcessStripeEvent.md`).

`stripe events resend` of the `succeeded` event was logged as `already processed`, and the row counts didn't change. A throwaway script peeked the queue and its dead-letter after each step and found both empty.
