# SqlTransientErrorDetector

`src/Payments.Infrastructure/Persistence/SqlTransientErrorDetector.cs`, implementing `ITransientErrorDetector` from `src/Payments.Application/Processing/ITransientErrorDetector.cs`.

Tells `ProcessStripeEvent` whether an exception from the database is worth retrying (section 6, task 3.1). The retry itself lives in the use case (see `docs/code/ProcessStripeEvent.md`). This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## The rule

An exception is transient when, walking down `InnerException`:

1. the first `SqlException` found was caused by a `SocketException`, or has at least one `SqlError` whose number is in the list below; or
2. a `TimeoutException` is found before any `SqlException`.

Anything else is not transient, including `OperationCanceledException`, `DbUpdateConcurrencyException` and primary key violations (2627), which the store already turns into results.

The walk is needed because EF Core wraps the error: a failure in `SaveChangesAsync` arrives as a `DbUpdateException` whose inner exception is the `SqlException`. A failure in a read or in `BeginTransactionAsync` arrives as the `SqlException` itself. The walk stops at the first `SqlException`, because its own inner exception (a `Win32Exception` for network errors) adds nothing.

`Errors` is checked instead of `Number` alone because one `SqlException` can carry several errors, and `Number` is only the first.

## The error numbers

| Numbers | Meaning |
| --- | --- |
| -2 | Command timeout (SqlClient's own number) |
| 258 | Connection timeout. Checked in the task 3.1 session: a refused connection (`127.0.0.1,1`) and an address that doesn't answer both end in 258, because SqlClient retries a refused connection until `Connect Timeout` runs out |
| `SocketException` inside the `SqlException` | The connection failed at the socket. Found when CI failed in task 3.1: on Linux, which is what the containers run, a refused connection fails at once with number 0 and a `SocketException` inside, instead of retrying until 258 as on Windows. A database container restarting refuses connections like that, so it has to count as transient. Number 0 alone isn't used, because SqlClient also uses it for errors that aren't about the network |
| 20, 64, 121, 233, 10053, 10054, 10060 | Connection broken or reset by the network or the server |
| 40, 53, 10061, 10065, 11001 | Server not found, refused or unreachable, or host name not resolved. Added after the review of tasks 3.1 and 3.2: on Windows, `dotnet run` of the worker outside compose reports these with a `Win32Exception` inside instead of 258 or a `SocketException`, and 11001 is what a DNS miss looks like while the `sqlserver` container restarts. 53 and 11001 can also mean a wrong server name, which no retry fixes, but the cost of retrying it is bounded: 4 attempts per delivery and the dead-letter after 5 deliveries |
| 1205 | Deadlock victim. Rare with `READ_COMMITTED_SNAPSHOT` (see `docs/code/PaymentsDbContext.md`), and retrying the whole transaction is the documented fix |
| 1222 | Lock request timeout. Added after the same review: another transaction held the lock longer than `LOCK_TIMEOUT`, and a later attempt finds it free |
| 4060, 4221, 40143, 40197, 40501, 40540, 40613, 42108, 42109, 49918, 49919, 49920 | Azure SQL: database unavailable, busy, reconfiguring, failing over or throttling. 40613 is also what a paused free database returns while it resumes |
| 10928, 10929 | Azure SQL resource limits reached |

The Azure SQL numbers come from Microsoft's "Troubleshoot transient connection errors" page for Azure SQL Database. The list is short on purpose. A number that is missing makes the message fail its delivery and come back later (the worker abandons on any exception), so a gap costs a few seconds, not an event.

## Alternatives not used

- **`DbException.IsTransient`.** .NET added it as a virtual property, but `Microsoft.Data.SqlClient` 6.1 doesn't override it, so it is always `false` for a `SqlException` (checked in the task 3.1 session: the name doesn't appear in the SqlClient assembly).
- **EF Core's `SqlServerTransientExceptionDetector.ShouldRetryOn`.** It has a fuller list, but it lives in a `.Internal` namespace. Using it raises warning EF1001, which is an error here (warnings as errors), and internal APIs can change in any minor version. Suppressing the warning to save twenty numbers isn't worth it.
- **Polly.** A library for a loop with three delays and one filter would be harder to explain than the loop.

## Registration

A singleton in the worker's `Program.cs`. It has no state, and the use case is scoped, so either lifetime works; a singleton avoids one allocation per message.

## Tests

`tests/Payments.Infrastructure.Tests/Persistence/SqlTransientErrorDetectorTests.cs`:

- `Connection_that_cannot_reach_the_server_is_transient` opens a real `SqlConnection` to `127.0.0.1,1` with `Connect Timeout=1`, so it gets a real `SqlException` without Docker: 258 on Windows, number 0 with a `SocketException` on Linux (CI), and both must be transient (`SqlException` has no public constructor). It also checks the same exception wrapped in a `DbUpdateException`.
- `TimeoutException`, bare and wrapped, is transient; `InvalidOperationException`, `OperationCanceledException` and a `DbUpdateException` without a SQL error are not.
- `IsTransientErrorNumber` is public so that numbers that a test can't produce for real (deadlock, Azure SQL) are checked one by one, together with numbers that must stay permanent (2627 primary key, 547 foreign key, 207 invalid column).

Retrying against a real SQL Server whose connection drops mid-transaction is not tested here: see the task 3.1 item for 3.5 in `docs/pending.md`.
