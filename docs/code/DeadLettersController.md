# DeadLettersController

`src/Payments.Api/Controllers/DeadLettersController.cs`

The dead-letter endpoints of the dashboard (architecture sections 6 and 8, task 3.4). An adapter: it calls `ListDeadLetters` and `ReprocessDeadLetter` (see `docs/code/ReprocessDeadLetter.md`) and maps the result to HTTP. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

| Endpoint | Key | Responses |
| --- | --- | --- |
| `GET /api/dead-letters` | No | `200` with up to 100 messages |
| `POST /api/dead-letters/{sequenceNumber}/reprocess` | `X-Admin-Key` | `202`, `404`, `401` |

## Why the list needs no key

Section 8: the dashboard is public and read-only, because all data is test data, and only actions that change something require `X-Admin-Key`. Listing changes nothing. The list doesn't carry the event bodies (`docs/code/ServiceBusDeadLetterQueue.md`).

## Why `202` and not `200` or `204`

The message is back in the queue, not processed. The worker picks it up later and may send it to the dead-letter again. `202 Accepted` says exactly that.

## Why the route takes the sequence number

It is the only unique identifier of a dead-lettered message; the event ID can repeat. See `docs/code/ServiceBusDeadLetterQueue.md`. The `:long` constraint makes a non-numeric value a `404` from routing.

## The admin key

`[RequireAdminKey]` on the action. See `docs/code/RequireAdminKeyAttribute.md`.

## Returning the Application record

`ListAsync` returns `IReadOnlyList<DeadLetter>` straight from Application, without a response DTO in the API. The record has only the fields the screen needs, all primitive, so a copy would be the same type twice. If the screen ever needs a different shape, a DTO goes here.

## Tests

`tests/Payments.Api.Tests/DeadLetters/DeadLettersControllerTests.cs`, with `WebApplicationFactory` and `InMemoryDeadLetterQueue`: the list without key, `202` with the key, `404` for an unknown number, and `401` with no key, an empty key, a wrong key and the right key with a trailing space, with nothing reprocessed.
