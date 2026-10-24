# PaymentReader

`src/Payments.Infrastructure/Persistence/PaymentReader.cs`

The EF Core implementation of `IPaymentReader`, the port of the read API (task 5.1). It reads `Payments`, `PaymentHistory` and `InboxEvents` and never writes. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## The list query

```sql
SELECT TOP(@p) [p].[PaymentIntentId], [p].[Status], [p].[Amount], [p].[AmountRefunded], [p].[Currency], [p].[UpdatedAt]
FROM [Payments] AS [p]
WHERE [p].[UpdatedAt] < @updatedAt OR ([p].[UpdatedAt] = @updatedAt AND [p].[PaymentIntentId] < @paymentIntentId)
ORDER BY [p].[UpdatedAt] DESC, [p].[PaymentIntentId] DESC
```

This is the SQL EF generated in the integration test (the first page has no `WHERE`). It is keyset pagination: the page starts after the last row of the previous one instead of skipping a number of rows. `OFFSET n` makes the server read and throw away `n` rows on every page, so page 100 costs 100 pages of reads. It also shifts when a row is added: a payment updated while the operator reads moves to the top and pushes every later row one place down, so the next page repeats one. The keyset predicate reads only the rows it returns, through the index.

- **`PaymentIntentId` as tie-breaker:** many payments can share an `UpdatedAt` (the `flood` mode makes hundreds a second). Ordering by `UpdatedAt` alone leaves their order undefined, and a page boundary inside a tie would skip or repeat rows. The primary key makes the order total.
- **`string.Compare(...) < 0`:** the only way to write `<` on strings in LINQ; EF translates it to SQL `<`. The comparison uses the column's collation, not .NET ordinal order. That is fine because the cursor's comparison and the `ORDER BY` both run in SQL, so they agree. The in-memory test reader uses ordinal order, and the test IDs sort the same way in both.
- **Parameter types:** EF types `@paymentIntentId` as `varchar(64)` (`AnsiString`, `Size = 64`) from the column mapping, and `@updatedAt` as `datetime2` through `UtcDateTimeOffsetConverter`. An `nvarchar` parameter against a `varchar` column would force an implicit conversion that can turn a seek into a scan.
- **Not a snapshot either:** a payment on a later page that is updated while the operator pages moves above the cursor, so this pass doesn't show it again. It never repeats a row or shifts the page, which is what `OFFSET` gets wrong, and the hub's `PaymentUpdated` (task 5.3) brings the change to the screen.
- **`AsNoTracking`:** the rows are only projected into records. Tracking would keep snapshots for change detection that never runs.

## The index

`IX_Payments_UpdatedAt_PaymentIntentId` on (`UpdatedAt`, `PaymentIntentId`), migration `AddPaymentsUpdatedAtIndex` (see `docs/code/PaymentsDbContext.md`). It is ascending: SQL Server reads an index backwards for `ORDER BY ... DESC` at the same cost, and a descending index would only matter for a mixed-direction order. It doesn't include the other columns, so each returned row costs a key lookup on the clustered primary key. With at most 101 rows per page that is a few hundred page reads, not worth covering the index and doubling the size of every write the worker makes.

## The detail query

Two queries: the payment by primary key, then its history joined to `InboxEvents` by `EventId` and ordered by `PaymentHistory.Id`.

- **Order by `Id`, not `OccurredAt`:** `Id` is the write order (section 5), the order the worker processed the events. Two events processed in the same tick would tie on `OccurredAt`.
- **Inner join:** every history row is written in the same transaction as its inbox row (section 5, "Inbox and state in the same transaction"), so the join never drops a row. It brings `EventType`, which the timeline shows, and `TraceId`, which the detail screen links to (section 7); `TraceId` is null until task 5.6.
- **No transaction around the two queries:** under read committed snapshot, each query sees committed data as of its start. An event committed between the two would show in the history but not in the payment's state. The dashboard reloads on every notification (section 7), so the next read is consistent, and a transaction would buy nothing a reader can see.

## Running as the API identity

In the cloud the API connects with its managed identity, which has only `db_datareader` (section 8). Task 5.1 checked it: signed in as the Entra admin, `EXECUTE AS USER = 'id-payment-webhooks-api'` read `Payments` and an `INSERT` failed with error 229 (`The INSERT permission was denied`), inside a transaction that was rolled back. Locally the API connects as `sa`, like the worker, because the compose SQL Server has no other login.

The API's `DbContext` uses `EnableRetryOnFailure`. Reads are idempotent, and a paused free database refuses the first login (section 10, task 4.10), so without the retry the first dashboard load after a pause would be a `500`. The worker doesn't use the retry strategy because it has its own retry (`docs/code/ProcessStripeEvent.md`) and user transactions, which the strategy doesn't allow.

## Tests

`tests/Payments.IntegrationTests/Reading/PaymentReaderTests.cs`, against SQL Server in Testcontainers (`PaymentsDatabaseFixture`, SQL only, migrated with the real migrations, tables cleared before each test). Its collection, `PaymentsDatabaseCollection`, has `DisableParallelization = true`, so xUnit runs it alone after the parallel collections. In one of two full runs with the class running in parallel, both `ProcessStripeEventBrokenConnectionTests` failed: they pause a SQL container with a 3 s command timeout, and one more SQL Server starting at the same time slowed the machine enough to break them. They passed alone and on `main` (2 of 2), and with the collection serialized the full suite passed 3 of 3.

- 25 payments in groups of four sharing an `UpdatedAt` with sub-second ticks, read through `ListPayments` four at a time. It visits all 25 once, in order, with the exact `UpdatedAt` values, in 7 queries, and none of them contains `OFFSET`; all contain `TOP`. The commands are captured with `LogTo` on `CommandExecuted`. Changing the tie-breaker to `<=` fails the test.
- The detail returns only its payment's history, interleaved with another payment's, in write order, with event type and trace ID.
- An unknown payment returns `null`.
