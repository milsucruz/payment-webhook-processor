# PaymentStore

`src/Payments.Infrastructure/Persistence/PaymentStore.cs`

EF Core implementation of `IPaymentStore`, the port that `ProcessStripeEvent` uses to write inbox, payment and history in one transaction (see `docs/code/ProcessStripeEvent.md`). This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## One port, not three

Section 11 used to list a repository port and an inbox port. The transaction has to cover both, so they would need a third, unit-of-work port, and all three would have to share one `DbContext`. One port with six methods is simpler to explain and to replace with a test double. Decided in the task 2.4 session; section 11 now says "payment store".

## The transaction

`BeginTransactionAsync` opens a database transaction and returns it as `IAsyncDisposable`. Disposing it without `CommitAsync` rolls back, which is how every early return of the use case (conflict, duplicate, exception) leaves nothing behind. `CommitAsync` saves the pending changes and commits.

The isolation is the default read committed, with `READ_COMMITTED_SNAPSHOT` on both locally and in Azure SQL (`docs/code/PaymentsDbContext.md`). The read of the payment doesn't block, and a concurrent change shows up on the write.

## Two `SaveChanges`, and why the duplicate needs no table check

| Call | What is pending | What can fail |
| --- | --- | --- |
| `TryAddInboxEventAsync` | The inbox row, plus the `UPDATE` of an existing payment that `Apply` changed in memory | Inbox primary key (duplicate), `RowVersion` (`DbUpdateConcurrencyException`) |
| `CommitAsync` | The new payment, if unknown, and the history row | `Payments` primary key (concurrent creation) |

A primary key violation (SQL error 2627) in the first call can only be the inbox, because a new payment hasn't been added yet, and the update of an existing one can't violate a key. So the store catches only that error, and only there, and returns `false`. Everything else propagates:

- `DbUpdateConcurrencyException` is a different exception type, not a `SqlException`, so the `when` filter lets it through.
- A `Payments` key violation only happens in `CommitAsync`, which catches nothing.

Task 2.6 turns those two into a retry. Telling the tables apart by name is not needed as long as this order holds.

The payment `UPDATE` rides along in the first `SaveChanges` because `Apply` decides and changes in one call: the outcome that goes into the inbox row comes from it. The alternative (decide with `PaymentTransitions.Decide`, insert the inbox alone, then `Apply`) would compute the decision twice. Inside one transaction the order of the writes doesn't change the result.

When the inbox insert fails, EF rolls the batch back to a savepoint, and the `DbContext` keeps the failed entries as tracked. Nothing uses the context afterwards: the use case returns, the transaction rolls back, and the worker (task 2.5) creates one scope per message.

## Lifetime

The store holds the open transaction in a field, so it lives in the same scope as its `DbContext`: one instance per message. It is not registered anywhere yet; the worker registers it in task 2.5.

## Not tested against SQL Server yet

The unit tests use a test double (`InMemoryPaymentStore`). This class meets a real database first in task 2.5, when `stripe trigger` must change the local database, and the duplicate case gets an integration test with the rest of the integration suite (see `docs/pending.md`).
