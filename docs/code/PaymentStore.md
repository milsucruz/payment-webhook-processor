# PaymentStore

`src/Payments.Infrastructure/Persistence/PaymentStore.cs`

EF Core implementation of `IPaymentStore`, the port that `ProcessStripeEvent` uses to write inbox, payment and history in one transaction (see `docs/code/ProcessStripeEvent.md`). This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## One port, not three

Section 11 used to list a repository port and an inbox port. The transaction has to cover both, so they would need a third, unit-of-work port, and all three would have to share one `DbContext`. One port with six methods is simpler to explain and to replace with a test double. Decided in the task 2.4 session; section 11 now says "payment store".

## The transaction

`BeginTransactionAsync` opens a database transaction and returns it as `IAsyncDisposable`. Disposing it without a successful `CommitAsync` rolls back, which is how every early return of the use case (conflict, duplicate, concurrency conflict, exception) leaves nothing behind. `CommitAsync` saves the pending changes and commits only if the save worked.

`BeginTransactionAsync` first calls `ChangeTracker.Clear()`. The use case opens one transaction per attempt on the same store (task 2.6), and `FindAsync` returns an entity the context already tracks without going to the database. Without the clear, a retry would get back the `Payment` that the failed attempt loaded and changed with `Apply`, not the row the other writer just committed, and would lose again. The other way to get a clean context, a new DI scope per attempt, would need the worker to catch and loop, which the layering rule forbids.

The isolation is the default read committed, with `READ_COMMITTED_SNAPSHOT` on both locally and in Azure SQL (`docs/code/PaymentsDbContext.md`). The read of the payment doesn't block, and a concurrent change shows up on the write.

## Two `SaveChanges`, one translation

| Call | What is pending | What can fail | Result |
| --- | --- | --- | --- |
| `AddInboxEventAsync` | The inbox row, plus the `UPDATE` of an existing payment that `Apply` changed in memory | Inbox primary key; `RowVersion` | `DuplicateEvent`; `ConcurrencyConflict` |
| `CommitAsync` | The new payment, if unknown, and the history row | `Payments` primary key (concurrent creation) | `ConcurrencyConflict` |

Both calls go through the private `SaveAsync`, which turns the expected failures into a `PaymentStoreWriteResult` and lets everything else propagate (transient errors are task 3.1):

| Exception | Result |
| --- | --- |
| `DbUpdateConcurrencyException` (the `UPDATE ... WHERE RowVersion = @old` touched no row) | `ConcurrencyConflict` |
| SQL error 2627 on `PK_InboxEvents` | `DuplicateEvent` |
| SQL error 2627 on `PK_Payments` | `ConcurrencyConflict` |

The order of the calls already decides which key a 2627 can be (a new payment is only added after the inbox is saved), so checking the name is redundant today. It is there because section 6 and the task ask for the table to tell them apart, and because it keeps working if the order changes. The name is not hard-coded: it comes from the EF model (`FindPrimaryKey().GetName()`), the same place the migration took it from, and is looked for between quotes in the `SqlException` message ("Violation of PRIMARY KEY constraint 'PK_Payments'..."). The message text can be translated with the login's language, but the constraint name inside it isn't.

`DbUpdateConcurrencyException` derives from `DbUpdateException`, so its `catch` comes first. The two `when` filters only match a 2627 on a known key; any other `DbUpdateException` propagates.

### Why the conflict only shows up after the other commit

With `READ_COMMITTED_SNAPSHOT`, both runs read the same version of the payment without blocking. The writes do block: the loser's `UPDATE` (or `INSERT` of the same key) waits for the winner's lock, and fails only once the winner commits. That is why the use case can retry right away, with no wait: the next read already sees the committed row.

The payment `UPDATE` rides along in the first `SaveChanges` because `Apply` decides and changes in one call: the outcome that goes into the inbox row comes from it. The alternative (decide with `PaymentTransitions.Decide`, insert the inbox alone, then `Apply`) would compute the decision twice. Inside one transaction the order of the writes doesn't change the result.

When the inbox insert fails, EF rolls the batch back to a savepoint, and the `DbContext` keeps the failed entries as tracked. Nothing uses the context afterwards: the use case returns, the transaction rolls back, and `PaymentEventsWorker` creates one scope per message.

## Lifetime

The store holds the open transaction in a field, so it lives in the same scope as its `DbContext`: one instance per message. The worker registers it as scoped in `Program.cs`.

## Tests against SQL Server

The unit tests use a test double (`InMemoryPaymentStore`). `tests/Payments.IntegrationTests/Processing/ProcessStripeEventConcurrencyTests.cs` (task 2.6) runs this class against a real SQL Server in Testcontainers and covers both `ConcurrencyConflict` paths: `RowVersion` (existing payment) and `PK_Payments` (unknown payment). The `DuplicateEvent` path against SQL Server is covered since task 3.5 by `Duplicate_delivery_writes_one_row_and_rolls_back` (see `docs/code/PaymentEventsWorker.md`), which checks the "already processed" log that only error 2627 on `PK_InboxEvents` produces.
