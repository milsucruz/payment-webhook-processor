# DeadLettersController

`src/Payments.Api/Controllers/DeadLettersController.cs`, with `AdminKeyFilter` and `AdminOptions` in `src/Payments.Api/Authentication/` and the use cases `ListDeadLetters` and `ReprocessDeadLetter` in `src/Payments.Application/DeadLetters/`.

The administrative API over the dead-letter of `payment-events` (architecture section 6, "Reprocessing", and task 3.4). This document holds the detail that the code leaves out. Keep it in sync when the classes change.

## Endpoints

| Endpoint | Key | Result |
| --- | --- | --- |
| `GET /api/dead-letters` | No | `200` with up to 100 dead-lettered messages, oldest first, without removing them |
| `POST /api/dead-letters/{sequenceNumber}/reprocess` | `X-Admin-Key` | `204` when the message went back to the queue, `404` when it is not in the dead-letter, `401` without the right key |

### Why only the POST needs the key

Section 8 says the dashboard is public and read-only because every record is test data, and only actions that change something need `X-Admin-Key`. Listing changes nothing, so it is public like the rest of the dashboard. The 401 in the task's done criterion is the reprocess one.

### Why the sequence number, not the event ID

The `MessageId` is the `evt_` ID, and the same event can be in the dead-letter twice: Stripe resends an event, or an operator reprocesses a message that fails again. The sequence number is assigned by Service Bus and is unique within the entity, so it names exactly one message. The dead-letter subqueue gives a dead-lettered message a new sequence number, which is the one the list shows and the POST takes. A reprocessed message gets another one when it comes back (the end-to-end run of task 3.4 shows 1, then 2).

### What the list returns

`DeadLetterMessage`: sequence number, `messageId`, `reason` (`DeadLetterReason`), `errorDescription` (`DeadLetterErrorDescription`), `abandonReason`, `deliveryCount` and `enqueuedAt`.

- **No body.** The adapter never reads the event (layering rule in `CLAUDE.md`), and the dashboard screen (section 7) shows the reason and a button. The body is still in the dead-letter, and reprocessing sends it unchanged.
- **`abandonReason`** is the `AbandonReason` application property that the worker writes when it abandons a message after a transient failure or a concurrency conflict (see `docs/code/PaymentEventsWorker.md`). A message dead-lettered as `MaxDeliveryCountExceeded` without this property never failed on the database: it was a poison message (an exception, which the SDK abandons with no properties). With it, the `Delivery N:` prefix says when the database last failed; `Delivery 5:` means the last delivery was a database failure, a lower number means the later ones failed another way.
- **`deliveryCount` is a peek value.** Peeking doesn't count as a delivery, so a message that was dead-lettered on its first delivery shows `0` (the emulator in task 3.4), and a receive would show one more. Reprocessing scans also add one to the messages they hold and abandon (see `docs/code/ServiceBusDeadLetterQueue.md`).
- **At most 100** (`ListDeadLetters.MaxMessages`). No pagination yet: the dead-letter is expected to be empty almost always, and the alert of section 6 fires at one message. Pagination can be added with the screen (task 5.x) if needed.

## Layering

The controller is an adapter, like the webhook controller: it calls a use case and maps the result to a status code, with no `try`/`catch` and no reference to Infrastructure (`ControllerDependencyTests`).

- `ListDeadLetters` only asks the `IDeadLetterQueue` port for 100 messages. It exists because `CLAUDE.md` makes the controller call a use case, never a port.
- `ReprocessDeadLetter` calls the port and turns its answer into `ReprocessDeadLetterResult` (`Reprocessed`, `NotFound`). It logs both outcomes: information with the sequence number and the `evt_` ID, warning for not found. The port returns the `MessageId` (or `null`) instead of a `bool` so that the log names the event, which is what an operator searches for.
- A Service Bus failure is not caught. It leaves the action, ASP.NET Core logs it as an error and answers `500`. Unlike the webhook, where `500` is a contract with Stripe and the use case must return it on purpose, nobody relies on this status, and the use case has nothing better to do with the exception.

## `AdminKeyFilter`

An MVC authorization filter, applied with `[ServiceFilter<AdminKeyFilter>]` and registered as a singleton, so it gets `IOptions<AdminOptions>` by constructor instead of looking it up.

- **Comparison in constant time.** Both the configured key and the header value are hashed with SHA-256 and compared with `CryptographicOperations.FixedTimeEquals`. A plain `==` stops at the first different character, which leaks how much of a guess is right. `FixedTimeEquals` alone returns early on a different length; hashing first makes both inputs 32 bytes.
- **Missing, empty or wrong header: all `401`.** A missing header reads as an empty string, which never matches because the configured key can't be empty. `401` (not `403`) because the key is the only identity the request has.
- **`Admin:ApiKey` is required at startup** (`ValidateOnStart`), like `Stripe:WebhookSecret`. An empty key must stop the API, not leave the endpoint open or always closed. Locally it comes from `ADMIN_API_KEY` in `.env` through compose; in the cloud from the Key Vault secret `admin-api-key` (section 8, pending for task 4.5).

### Why a filter and not an authentication handler

Task 3.4 started with an `AuthenticationHandler` (scheme `AdminKey`) and `[Authorize]`, the framework's standard mechanism. `AddAuthentication()` also registers ASP.NET Core Data Protection, whose hosted service creates a key ring at startup. In the container, with no persistent key storage, that logs two warnings on every start (`Storing keys in a directory ... that may not be persisted` and `No XML encryptor configured`), which would show up in Application Insights on every cold start, for a feature this API never uses. Registering only `AddAuthenticationCore()` and the scheme by hand avoided Data Protection, but then the handler failed to build (`UrlEncoder` missing), and copying the rest of `AddAuthentication` piece by piece is framework trivia nobody should have to defend. A filter does the one thing needed, a shared key on one action, in about 20 lines. When Entra ID login arrives (section 12, "Real login"), it brings the authentication middleware with it, and this filter goes away.

## Tests

`tests/Payments.Application.Tests/DeadLetters`: the list asks the port for 100; reprocess returns `Reprocessed` and logs the event ID, or `NotFound` and logs a warning. They use `FakeDeadLetterQueue`, which `tests/Payments.Api.Tests` links.

`tests/Payments.Api.Tests/DeadLetters/DeadLettersControllerTests` runs the real pipeline with `WebApplicationFactory` and the fake queue: the list without a key (`200`, nothing removed), reprocess with the key (`204`), an unknown sequence number (`404`), and a missing, empty, wrong or padded key (`401`, nothing reprocessed).

The Service Bus side is covered by `ServiceBusDeadLetterQueueTests` (see its class document), and task 3.4 ran the done criterion through compose: a signed `customer.created` event dead-lettered as `InvalidEvent`, `401` with no key and with a wrong key, `204` with the key while the worker was stopped, `404` on a second call, an empty list, then the worker restarted, processed the event again and dead-lettered it with a new sequence number.
