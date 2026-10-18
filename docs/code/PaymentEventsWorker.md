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
| `ConcurrencyConflict` | Abandon | The use case already retried from the read `ProcessStripeEvent.MaxConcurrencyAttempts` times (task 2.6). Losing that many races in a row is unlikely but not a broken assumption, so the message goes back for a later delivery |
| `TransientFailure` | Abandon | The use case already made 1 attempt plus 3 retries over about 6 s (task 3.1, `docs/code/ProcessStripeEvent.md`). The database is still unreachable, so the message goes back and Service Bus delivers it again, up to 5 times. Not a permanent error |

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

`ServiceBusOptions` is validated on start, as in the API. The connection string is checked in `Program.AddPaymentEventsWorker` before `AddDbContext`, so a missing value stops the worker at startup instead of at the first message.

`appsettings.Development.json` lowers `Microsoft.EntityFrameworkCore` to `Warning`. At `Information`, EF logs every SQL command, three or four per message, and buries the use case's one line per event. Errors from EF still show up.

In compose the worker waits for `migrator` to finish successfully and for `servicebus` to start, so it never meets a database without the schema.

## Tests

`tests/Payments.Worker.Tests/WorkerDependencyTests.cs` checks that no type in the worker, apart from `Program`, takes or holds a `Payments.Infrastructure` type. It is the same check as `ControllerDependencyTests` in the API. Adding a `PaymentStore` constructor parameter to the worker makes it fail.

There is no unit test of the settlement mapping: building fake `ProcessMessageEventArgs` costs more than the `switch` it would check. It is checked against the real Service Bus emulator instead.

The integration tests in `tests/Payments.IntegrationTests/Worker/` run the real worker against SQL Server and the emulator in Testcontainers, with the registrations of `Program.AddPaymentEventsWorker` (see "Registrations" below and `docs/code/WorkerFixture.md`):

| Test | Scenario | Asserts |
| --- | --- | --- |
| `PaymentEventsWorkerDeadLetterTests` (task 3.2) | A body that is `not json` | Dead-letter with the same `MessageId` and body, reason `InvalidEvent`, the parser message, `DeliveryCount` 0 (first delivery). Putting `InvalidEvent` back on the abandon path fails it with `MaxDeliveryCountExceeded` (task 3.2 session) |
| `Duplicate_delivery_writes_one_row_and_rolls_back` (3.5) | The same message delivered twice, one after the other | One inbox row, one history row, the payment's `RowVersion` unchanged by the second delivery, "already processed" logged (error 2627 on `PK_InboxEvents` turned into `DuplicateEvent`), nothing in the dead-letter |
| `Poison_message_reaches_the_dead_letter_after_5_deliveries` (3.5) | `demo_fault=poison`, fault injection on | `MaxDeliveryCountExceeded`, `DeliveryCount` 5, no payment or inbox row |
| `Impossible_conflict_reaches_the_dead_letter_on_the_first_delivery` (3.5) | "canceled", then "paid" for the same payment | Reason `ImpossibleConflict` (literal), the use case's text as description, `DeliveryCount` 0, payment still canceled, only the canceled event in the inbox |
| `Crash_after_commit_does_not_duplicate_on_redelivery` (3.5) | The store throws once right after a successful commit | The message comes back, the inbox makes it a duplicate, it is completed; one inbox and one history row, nothing in the dead-letter. Completing the message before calling the use case fails it (mutation check in the task 3.5 session, see `docs/code/WorkerFixture.md`) |

`DeliveryCount` is read with a peek, as the dead-letter screen reads it. In the emulator a peek counts the deliveries that ended without a settlement, so a message the worker dead-letters on its first delivery shows 0, and a receive would show 1 (task 3.5; see `docs/code/WorkerFixture.md`).

## Registrations

`Program.AddPaymentEventsWorker(services, configuration)` holds every registration of the worker, and the top-level statements only build the host around it. It is a public static method on a `public partial class Program` in the same file, not an extension method in its own class, because `CLAUDE.md` lets `Payments.Infrastructure` appear only in `Program.cs`. The integration tests call it, so a changed registration (processor options, the fault injector switch) is what the tests run, instead of a copy that would drift (task 3.5; it replaced the hand-built processor of the task 3.2 test).

## Verified in task 2.5

`stripe trigger payment_intent.succeeded` sends `payment_intent.succeeded` and `payment_intent.created` in the same second, for a payment the database doesn't know yet. Both handlers ran at once:

- `succeeded` created the payment as paid.
- `created` read no payment too, tried to insert it, and failed with a `PK_Payments` violation in `CommitAsync`. That is the concurrent-creation case of task 2.6. The exception went back to the queue, and on redelivery `created` found the paid payment and was recorded as out of order.

The final state is correct: one payment, two inbox rows, two history rows. The history IDs are 1 and 3, because the rolled-back insert used identity 2. At that point this race logged a full stack trace as an error. Since task 2.6 the use case retries it from the read inside the same delivery (see `docs/code/ProcessStripeEvent.md`).

`stripe events resend` of the `succeeded` event was logged as `already processed`, and the row counts didn't change. A throwaway script peeked the queue and its dead-letter after each step and found both empty.
