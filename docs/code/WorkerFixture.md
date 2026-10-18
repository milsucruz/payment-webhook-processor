# WorkerFixture

`tests/Payments.IntegrationTests/Worker/WorkerFixture.cs`, with `WorkerCollection`, `CollectingLoggerProvider` and `CrashAfterCommitPaymentStore` in the same folder.

The shared setup of the worker's integration tests (task 3.5, architecture section 11: "a duplicate delivery writes one row; a poison message reaches the dead-letter after 5 deliveries, while an impossible conflict reaches it on the first delivery; a crash after commit doesn't duplicate"). The scenarios themselves are listed in `docs/code/PaymentEventsWorker.md`. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## One SQL Server for the emulator and the database

The fixture starts one SQL Server and gives it to the emulator with `WithMsSqlContainer`, on a Docker network where the SQL Server answers as `sqlserver`. The `Payments` database lives in the same instance, migrated once. That is the compose layout (the emulator keeps its own databases in the instance the app uses), and it starts two containers instead of three.

## Shared through a collection fixture

`[Collection(WorkerCollection.Name)]` makes xUnit create the fixture once for every class in the collection and run those classes one after the other. The containers take most of the suite's time, so the five worker tests pay for them once. Running them one after the other also means only one worker is consuming the queue at a time.

Each test isolates itself instead of getting fresh containers:

- **Its own IDs.** Every test uses its own `pi_` and `evt_` values and filters the database by them.
- **An empty queue to start.** `InitializeAsync` drains the queue and its dead-letter in receive-and-delete mode, and keeps draining until a peek of both comes back empty. A receive can't see a message that is still locked from an earlier test; the peek can, so the purge waits for it instead of leaving it for the next test (found in code review).
- **Its own worker.** Each test starts a host and stops it before asserting, so no handler is still running and the logs belong to that test. The fixture keeps every host it started, and the test's `DisposeAsync` stops them too, so a test that fails before its own stop doesn't leave a worker holding a lock (found in code review).

The other integration classes (publisher, dead-letter queue, concurrency) keep their own containers: two of them read "the" message of the queue and the third counts rows over the whole database. Sharing them would mean filtering by ID in each; the M3 review dropped it because the whole suite ran in 1.4 min in CI. `ProcessStripeEventBrokenConnectionTests` (task 3.7) can never share: it pauses its SQL Server.

## The worker host

`StartWorkerAsync` builds a host with `DisableDefaults`, so no `appsettings.json` or environment variable leaks into the test, adds the containers' connection strings as in-memory configuration, and calls `Program.AddPaymentEventsWorker`, the same method the real worker calls. A test can replace registrations afterwards: the last registration wins.

The logger provider is optional. `CollectingLoggerProvider` keeps every log message, which is how the duplicate and crash scenarios see "already processed", the proof that the second delivery went through error 2627 to `DuplicateEvent`.

## Waiting without fixed sleeps

- One sender and two peek receivers (queue and dead-letter) live as long as the fixture. A receiver remembers where its last peek stopped, so every peek passes `fromSequenceNumber: 0` to start from the oldest message. The first version opened a new link per poll, up to hundreds per wait (code review).
- `WaitUntilQueueIsEmptyAsync` peeks the queue every 200 ms until it is empty. A peek also returns locked messages, so an empty peek means every message was completed or dead-lettered, including a redelivery after an abandon.
- `PeekDeadLetterAsync` peeks the dead-letter every 200 ms until a message is there.

Both give up after 150 s. The poison scenario failed once in 13 local runs of the whole suite after 60 s, which is the lock duration: most likely one abandon was lost under load and the delivery waited for its lock to expire. That only delays the scenario: the message still reaches the dead-letter after 5 deliveries. So the wait is above one lock duration. The cause was never proven and the failure never came back: the 15 CI runs since task 3.5 and the local runs of tasks 3.6 and 3.7 all passed. The second M3 review dropped the investigation, since it could not be reproduced. The limit to know: 150 s covers up to two lost abandons (60 s each), and a third would fail the test.

## Why the dead-letter is peeked, not received

The first version received the dead-lettered message. That had two problems:

1. **The lock stayed.** A received message that isn't completed stays locked for 60 s, so the next test's purge couldn't see it and found it later. Three scenarios failed on the first run for that reason.
2. **The count changed.** A receive is a new delivery. The poison message showed `DeliveryCount` 6 when received and 5 when peeked; the dead-lettered-on-first-delivery case showed 1 and 0.

A peek changes nothing and returns what `GET /api/dead-letters` returns. In the emulator, the peeked count is the number of deliveries that ended without a settlement: 5 abandons for a poison message, 0 for a message the worker dead-lettered on its first delivery. The tests assert 5 and 0, which still tell the two paths apart. Whether the cloud counts the same way is the delivery-counting risk in section 14 of the architecture.

## Simulating a crash after commit

`CrashAfterCommitPaymentStore` wraps the real `PaymentStore` and throws `InvalidOperationException` once, right after the first `CommitAsync` that saved. Every other call goes to the real store.

The exception is not transient, so `ProcessStripeEvent` lets it out, the handler throws and the SDK abandons the message. The next delivery inserts the same inbox row, gets error 2627, returns `Duplicate`, and the worker completes the message. From the queue's point of view this is a crash between the commit and the completion: the message comes back after its transaction committed.

Alternatives not used:

- **Killing the process.** The worker runs inside the test process, and a host stopped from outside cancels its handlers, which is a different path (cancellation before or after the commit).
- **Not settling and letting the lock expire.** Same effect, but the redelivery takes the full 60 s of the lock instead of about one second.

## Mutation check

Making the worker complete the message before calling the use case (auto-complete style) fails the crash scenario with `Expected: 1, Actual: 0`: the queue is empty before the event is processed, so the test stops the worker and finds no crash and no row. That empty-queue-before-commit window is where a real crash would lose the event, which is the rule "the worker completes only after the commit".

## The unreadable event no longer needs a dead database

Until task 3.5, the `InvalidEvent` test pointed the worker at a database that didn't exist, so any SQL call before the parser would have failed it. With the shared fixture the database is real, so the test now asserts that no inbox row exists for `evt_unreadable` instead (code review).

## Code review findings not applied

- **Peek through `ServiceBusDeadLetterQueue.PeekAsync`.** Its `DeadLetter` record has no body, and the `InvalidEvent` test checks that the body is kept as received. The raw SDK peek is the same call the class makes.
