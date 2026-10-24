# PaymentsController

`src/Payments.Api/Controllers/PaymentsController.cs`

The read endpoints of the dashboard's payments and payment detail screens (architecture sections 5 and 7, task 5.1). An adapter: it calls `ListPayments` (see `docs/code/ListPayments.md`) and `GetPayment` and maps the result to HTTP. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

| Endpoint | Key | Responses |
| --- | --- | --- |
| `GET /api/payments?limit=&cursor=` | No | `200` with `{ items, nextCursor }`; `400` for a bad `limit` or `cursor` |
| `GET /api/payments/{paymentIntentId}` | No | `200` with the payment and its `history`; `404` |

## Why no key

Section 8: the dashboard is public and read-only, and only actions that change something require `X-Admin-Key`. Same reasoning as `GET /api/dead-letters` (`docs/code/DeadLettersController.md`).

## Paging contract

- `limit` defaults to 50 (`ListPayments.DefaultPageSize`) and accepts 1 to 100 (`ListPayments.MaxPageSize`, the same cap as the dead-letter list). `[Range]` on the parameter makes `[ApiController]` answer `400` with a validation problem before the action runs, so the use case never sees an out-of-range value. A non-numeric `limit` is a `400` from model binding.
- `cursor` is the `nextCursor` of the previous page, passed back as is. Clients don't build or read it (`docs/code/PaymentCursor.md`). A value that doesn't decode is a `400` with the same validation problem shape, via `ModelState.AddModelError` and `ValidationProblem()`, so the client handles one error format.
- `nextCursor` is `null` on the last page.

The parameters keep their default values (`limit = 50`, `cancellationToken = default`) because C# requires every parameter after an optional one to be optional too; MVC uses the default when the query string doesn't carry the value.

## Enums as strings

`Program.cs` adds `JsonStringEnumConverter` to the controllers' JSON options, so `status`, `fromStatus` and `toStatus` come out as `"Paid"` instead of `5`. A number would tie the dashboard to the byte values of `PaymentStatus`, which exist for the database. The converter is global, but no other controller returns an enum. The SignalR hub (task 5.3) has its own JSON options, see `docs/pending.md`.

## Returning the Application records

Same choice as `DeadLettersController`: `PaymentPage`, `PaymentSummary`, `PaymentDetail` and `PaymentHistoryItem` carry only what the screens need, all primitive, so a response DTO would be a copy of the same type.

## Tests

`tests/Payments.Api.Tests/Payments/PaymentsControllerTests.cs`, with `WebApplicationFactory` and `InMemoryPaymentReader` (linked from the Application tests): first page newest first with a next cursor and string status, following the cursor to the last page (`nextCursor` null), the default page size, `400` with a problem body and no read for `limit=0`, `limit=101`, `limit=abc` and a bad cursor, the detail with history, and `404` for an unknown payment. The SQL itself is covered in `docs/code/PaymentReader.md`.
