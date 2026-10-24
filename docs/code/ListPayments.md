# ListPayments

`src/Payments.Application/Reading/ListPayments.cs`, with `GetPayment.cs`, the `IPaymentReader` port and the result records next to it.

The use cases of the read API (task 5.1). `ListPayments` returns one page of payments, most recently updated first; `GetPayment` returns one payment with its history. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Results

| Use case | Outcomes | Log |
| --- | --- | --- |
| `ListPayments` | `Listed` with a `PaymentPage`; `InvalidCursor` | Warning on `InvalidCursor` |
| `GetPayment` | `Found` with a `PaymentDetail`; `NotFound` | Information on `NotFound` |

Both follow the result rule in `CLAUDE.md`, like `ReprocessDeadLetterResult.NotFound`: an expected outcome is an enum, not `null` or an exception. A successful read isn't logged; with a dashboard reloading pages that would be noise, and task 5.6 traces every request anyway. A bad cursor is a warning because only a broken client sends one. An unknown payment is information because a stale link produces one.

## One extra row instead of a count

`ListPayments` asks the reader for `pageSize + 1` rows. If the extra row comes back, there is a next page, and the cursor is built from the last row that is returned, not from the extra one: the next page starts strictly after it. A `COUNT(*)` would cost a second query and still be wrong by the time the client asks for the next page.

## Page size guard

The controller already rejects a `limit` outside 1 to 100 with a `400`. The use case throws `ArgumentOutOfRangeException` for the same range anyway: a value outside it is a programming error in a caller, not an expected outcome, so it is not part of the result enum.

## Why the cursor is decoded here

The controller passes the cursor as the string it received, and the use case decodes it. The cursor's format is an Application detail (`docs/code/PaymentCursor.md`), and a bad cursor is one of the use case's outcomes. The controller only maps outcomes to status codes.

## Tests

`tests/Payments.Application.Tests/Reading/ListPaymentsTests.cs` and `GetPaymentTests.cs`, with `InMemoryPaymentReader`, which applies the same keyset rule with ordinal comparison: a page holding everything has no next cursor and asks for one extra row, the next cursor points at the last returned row, following cursors over payments that share `UpdatedAt` visits each one once in order, a bad cursor reads nothing and logs a warning, and both ends of the page size range throw. `GetPayment`: found without a log, not found with an information log.
