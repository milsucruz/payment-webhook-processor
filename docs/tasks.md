# Tasks

System design in [architecture.md](architecture.md). The "Done" column marks finished tasks (✅ done, ⬜ pending); a task gets ✅ only after its done criterion is verified, in the same commit as its session log row. The session log at the end of this file has the details.

The eight roadmap milestones break down into 48 tasks of 1 to 3 hours each, totaling 50 to 80 hours without the optional milestone. Each task fits in one session and has a done criterion that is verified by running something.

| Milestone | Tasks | Hours |
| --- | --- | --- |
| M0. Skeleton | 5 | 4.5 to 5.5 |
| M1. Ingestion | 6 | 5.5 to 8.5 |
| M2. Processing | 8 | 10 to 16 |
| M3. Failures | 5 | 6 to 9 |
| M4. Cloud | 9 | 10 to 17 |
| M5. Dashboard and telemetry | 8 | 9.5 to 17 |
| M6. Documentation | 4 | 4.5 to 7 |
| Total up to the done criterion | 45 | 50 to 80 |
| M7. Horizontal scaling (optional) | 3 | 2.5 to 4.5 |

The total came out above the initial estimate of 40 to 60 hours, which was made before the task breakdown. The breakdown is the better reference.

## How to use

- **One task per session.** Ask Claude Code for a short plan before any code, citing the task number and the section of the architecture document.
- **Read the diff.** Once the code is written, explain out loud what each part does.
- **Azure account only in M4.** From M0 to M3 everything runs on the local machine, and the 30-day credit only starts counting when the account is created.
- **Measure M0 and M1.** If together they take more than 12.5 hours, apply the same factor to the rest.

## M0. Skeleton (4.5 to 5.5 h)

| Done | # | Task | Hours | Done when |
| :---: | --- | --- | --- | --- |
| ✅ | 0.1 | Create the solution and the Domain, Application, Infrastructure, Api and Worker projects, with references in the right direction, `.editorconfig` and `.gitignore` | 1 | `dotnet build` passes and Domain references no other project |
| ✅ | 0.2 | Write the `docker-compose.yml` with SQL Server and the Service Bus emulator, plus the configuration for the `payment-events` and `payment-notifications` queues | 1 to 2 | `docker compose up` starts both and a test console sends and receives a message on a queue |
| ✅ | 0.3 | Write multi-stage Dockerfiles for the API and the worker and add them to the compose file | 1 | `docker compose up` starts everything and a `GET` on the API responds |
| ✅ | 0.4 | Create the test project with a trivial test and the `ci.yml` (restore, build, test) | 1 | A pull request is opened and CI goes green |
| ✅ | 0.5 | Copy the architecture into `docs/architecture.md` and write `CLAUDE.md` with the conventions | 0.5 | Claude Code correctly answers "what is the idempotency rule?" without you pasting anything |

Task 0.2 is the most uncertain one in the milestone. Setting up the emulator for the first time tends to get stuck on details, so don't leave it for the end of a session.

## M1. Ingestion (5.5 to 8.5 h)

| Done | # | Task | Hours | Done when |
| :---: | --- | --- | --- | --- |
| ✅ | 1.1 | Create `POST /webhooks/stripe`, reading the raw body, with a 256 KB limit, responding `200` for now | 1 | A `curl` with any body returns `200`, and a body above the limit is rejected |
| ✅ | 1.2 | Validate the signature with the official Stripe library, 5-minute tolerance, `400` if invalid | 1 to 2 | Tests with a valid signature, an invalid one and an old timestamp pass; tampering with one byte of the body makes validation fail |
| ✅ | 1.3 | Create the `IEventPublisher` port in Application and the Service Bus implementation: `MessageId` equal to the event ID, body equal to the original JSON | 1 to 2 | An integration test publishes and reads the message on the emulator with the same `MessageId` |
| ✅ | 1.4 | Wire the two together: publish before responding `200`, and `500` if publishing fails | 1 | With `WebApplicationFactory`: a valid signature returns `200` and publishes a message; a failing publisher returns `500` |
| ✅ | 1.5 | Run end to end with `stripe listen` and `stripe trigger payment_intent.succeeded`, and save the real JSONs in `tests/fixtures` | 0.5 to 1 | The message shows up on the local queue and the fixture files are in the repository |
| ✅ | 1.6 | Move ingestion into the `ReceiveStripeEvent` use case in Application, with the signature verifier port next to it, so the controller only translates HTTP | 1 to 1.5 | The `Payments.Api.Tests` tests pass with no assertion changed, the new application tests cover the four results, and the controller does not reference `Payments.Infrastructure` |

## M2. Processing (10 to 16 h)

| Done | # | Task | Hours | Done when |
| :---: | --- | --- | --- | --- |
| ✅ | 2.1 | Domain: `PaymentStatus`, levels, transition table and the four outcomes (applied, ignored as out of order, redundant, conflict) | 1 to 2 | Parameterized tests cover every combination of current state and event |
| ✅ | 2.2 | Parser from Stripe events to an internal type, using the fixtures | 1 | Each fixture becomes the expected internal event; an unknown type raises a permanent error |
| ✅ | 2.3 | EF Core: entities, mappings, `RowVersion`, first migration and the Migrator project | 1 to 2 | The compose setup applies the migration and the three tables exist |
| ✅ | 2.4 | `ProcessStripeEvent` use case: inbox, state and history in one transaction, with duplicates detected by the primary key | 2 to 3 | Tests with test doubles pass for duplicate, unknown payment and partial refund |
| ✅ | 2.5 | Worker: `ServiceBusProcessor` with manual completion after the commit | 1 to 2 | `stripe trigger` changes the payment in the local database and the message leaves the queue |
| ✅ | 2.6 | Concurrency: retry processing when the `RowVersion` check fails or the insert of a new payment violates the `Payments` primary key, telling it apart from the `InboxEvents` key, which means a duplicate | 1 | Two simultaneous runs on the same payment end with the correct state and two history rows, both for an existing payment and for an unknown one |
| ✅ | 2.7 | Property test: permutations and repetitions of valid event sequences | 1 to 2 | Every permutation leads to the same final state |
| ✅ | 2.8 | Simulator, `pay`, `refund` and `replay` modes | 2 to 3 | `replay` mode does not change the final state of any payment |

Tasks 2.1 and 2.7 are the ones that pay off most in interviews. It's worth writing the transition table by hand before asking for the code.

## M3. Failures (6 to 9 h)

| Done | # | Task | Hours | Done when |
| :---: | --- | --- | --- | --- |
| ⬜ | 3.1 | Classify errors as transient or permanent; retry with 1 attempt plus 3 retries (200 ms, 1 s, 5 s) and abandon the message | 1 to 2 | A test fails twice with a transient error and passes on the third attempt |
| ⬜ | 3.2 | Immediate dead-letter for permanent errors and impossible conflicts, with reason and description | 1 | An unreadable event shows up in the dead-letter with the reason filled in |
| ⬜ | 3.3 | Fault injection via the `demo_fault` metadata, behind the `FaultInjection:Enabled` setting | 1 | A `poison` payment reaches the dead-letter after 5 deliveries, and a `transient` one passes on the third |
| ⬜ | 3.4 | API: `GET /api/dead-letters`, without removing the messages, and a reprocess `POST`, with `X-Admin-Key` | 1 to 2 | The reprocessed message goes back to the main queue and disappears from the dead-letter; without the key, the API returns `401` |
| ⬜ | 3.5 | Integration tests with Testcontainers: duplicate delivery, poison message (after 5 deliveries) and impossible conflict (on the first delivery), and crash after commit | 2 to 3 | The four scenarios pass in CI |

At the end of M3 the system is complete on the local machine. It's a good point to rehearse the answer to "what happens if the worker crashes in the middle of processing?".

## M4. Cloud (10 to 17 h)

| Done | # | Task | Hours | Done when |
| :---: | --- | --- | --- | --- |
| ⬜ | 4.1 | Create the Azure account: MFA, a US$ 5 budget with alerts, resource group and region | 0.5 to 1 | The budget alert is active before any resource exists |
| ⬜ | 4.2 | Bicep: managed identities, Service Bus Basic with both queues and per-queue roles; dead-letter on expiration on for `payment-events` and off for `payment-notifications` | 1 to 2 | `what-if` comes out clean and the deploy creates the namespace and the queues, with dead-letter on expiration only on `payment-events` |
| ⬜ | 4.3 | Bicep: free SQL database with an Entra ID admin; the developer, signed in as the Entra ID admin, creates the contained users for the identities with a versioned script in `infra/` | 1 to 2 | Passwordless connection works with `az login`, and the option to pause when the free allowance runs out is on |
| ⬜ | 4.4 | Bicep: Key Vault, Log Analytics with a daily cap, and Application Insights | 1 | The secrets are stored and the 0.2 GB cap is configured |
| ⬜ | 4.5 | Bicep: Container Apps environment, API, worker and migration job, with images from GHCR and secrets referenced from Key Vault | 2 to 3 | `/health/live` responds at the public address |
| ⬜ | 4.6 | Replace connection strings with managed identity for Service Bus and SQL, through configuration only | 1 to 2 | No service secret appears in the container variables, and the worker writes to the database |
| ⬜ | 4.7 | Queue-length scale rule and liveness and readiness probes | 1 to 2 | With the queue empty the worker goes to zero, and the simulator's `flood` mode scales replicas up |
| ⬜ | 4.8 | `deploy.yml` with OIDC (Contributor plus conditioned Role Based Access Control Administrator): images, `what-if`, Bicep with the new tag on the migration job only, migration job, then the new API and worker revisions | 2 to 3 | A push to `main` publishes a revision tagged with the commit, with no secret stored in GitHub |
| ⬜ | 4.9 | Register the endpoint in the Stripe sandbox and store the `whsec_` in Key Vault | 0.5 to 1 | A test payment created in Stripe shows up in the cloud database |

This is the least predictable milestone, because it depends on trial and error on the real account. Tasks 4.6 and 4.7 validate two risks listed in the document: the role required by the KEDA rule and cold start.

To make the most of the 30-day credit, concentrate M4 and M5 in the weeks right after task 4.1.

## M5. Dashboard and telemetry (9.5 to 17 h)

| Done | # | Task | Hours | Done when |
| :---: | --- | --- | --- | --- |
| ⬜ | 5.1 | Read API: payments with keyset pagination, and detail with history | 1 to 2 | The API tests pass and pagination does not use `OFFSET` |
| ⬜ | 5.2 | Notification queue: the worker publishes after the commit, with a 60 s expiry, and the API consumes | 1 to 2 | A processed event produces a notification that the API receives |
| ⬜ | 5.3 | SignalR hub with the `PaymentUpdated`, `EventProcessed` and `QueueStats` messages | 1 to 2 | A test client connected to the hub receives all three messages |
| ⬜ | 5.4 | Dashboard: skeleton, payments screen and detail with timeline | 2 to 3 | The timeline shows applied and ignored events |
| ⬜ | 5.5 | Dashboard: live feed, reconnect with reload, and dead-letter screen | 2 to 3 | A test payment shows up on screen in under 3 s |
| ⬜ | 5.6 | OpenTelemetry and Serilog in both containers, with the Azure Monitor exporter | 1 to 2 | The trace of an event shows the six spans in Application Insights |
| ⬜ | 5.7 | Custom metrics and the two alerts | 1 to 2 | A `poison` payment triggers the dead-letter alert email |
| ⬜ | 5.8 | Check whether an open SignalR connection keeps the API replica from scaling to zero | 0.5 to 1 | The result is recorded in ADR 9 |

At the end of M5 the technical done criterion is met: the system runs in the cloud, with tests, dashboard and telemetry.

## M6. Documentation (4.5 to 7 h)

| Done | # | Task | Hours | Done when |
| :---: | --- | --- | --- | --- |
| ⬜ | 6.1 | README in English: what the system does, Mermaid diagram and how to run it locally | 1 to 2 | Someone who has never seen the project can bring the environment up by following the README |
| ⬜ | 6.2 | The ten ADRs, in English | 2 to 3 | Each ADR has context, decision, alternatives and consequences |
| ⬜ | 6.3 | A "what I would do differently" section and a short video or GIF of the demo | 1 | The README shows duplicates, out-of-order events and dead-letter happening |
| ⬜ | 6.4 | A recorded 3-minute rehearsal in English | 0.5 to 1 | You explain each piece and the worker crash without looking anything up |

Write the README and the ADRs yourself, and use Claude Code only to review. That is the written-English practice the plan calls for.

## M7. Horizontal scaling, optional (2.5 to 4.5 h)

| Done | # | Task | Hours | Done when |
| :---: | --- | --- | --- | --- |
| ⬜ | 7.1 | Azure SignalR Service on the free tier, via Bicep, and `AddAzureSignalR` in the API | 1 to 2 | The dashboard connects through the service |
| ⬜ | 7.2 | API with 1 to 2 replicas | 1 to 2 | Two browsers on different replicas receive the same update |
| ⬜ | 7.3 | Update ADR 9 with the change and the cost of the minimum replica | 0.5 | The ADR records the before, the after and the reason |

## Session log

At the end of each session, add a row. It's what the next session reads to know where the project stands.

| Date | Task | Status | Actual hours | Notes |
| --- | --- | --- | --- | --- |
| 2026-10-11 | 0.1 | Done | 0.5 | Solution with 5 layered projects; Directory.Build.props centralizes net10.0, nullable, warnings as errors |
| 2026-10-11 | 0.2 | Done | 0.5 | Compose with SQL Server 2022 (shared by emulator and app) + SB emulator 2.0.1. Local TTL 1 h (emulator cap). Emulator on host port 5673 because of a local RabbitMQ on 5672 |
| 2026-10-11 | 0.3 | Done | 0.3 | Multi-stage Dockerfiles for API and worker, wired into compose; GET on the API responds |
| 2026-10-11 | 0.4 | Done | 0.3 | tests/Payments.Domain.Tests with a test guarding Domain's dependencies; ci.yml (restore, build, test) on PRs. PR #1 green. Integration tests and Bicep join CI in 3.5 and M4 |
| 2026-10-11 | 0.5 | Done | 0.2 | Architecture and CLAUDE.md already in 5ad2722; clarified files not yet created (docs/adr in 6.2, tests/fixtures in 1.5, CONTEXT.md). Fresh `claude -p` answered the idempotency rule correctly, citing section 6. M0 complete |
| 2026-10-11 | 1.1 | Done | 0.4 | POST /webhooks/stripe reads the raw body, 256 KB limit via [RequestSizeLimit] (413, also for chunked). The action has no bound parameters (uses HttpContext.RequestAborted): any parameter, even a CancellationToken, made MVC parse form bodies before the raw read. Found in code review; replaced a first [Consumes] workaround. docs/pending.md created with the 256 KB vs Service Bus header-size item for 1.3/1.4 |
| 2026-10-12 | 1.2 | Done | 0.5 | Signature checked with Stripe.net EventUtility.ValidateSignature (no parsing; ConstructEvent would also fail on API version mismatch), 300 s tolerance, TimeProvider for exact boundary tests. IWebhookSignatureVerifier in Infrastructure/Webhooks; 400 with the reason logged only. API fails at startup without Stripe:WebhookSecret, so compose/.env.example got STRIPE_WEBHOOK_SECRET. New tests/Payments.Infrastructure.Tests (11 tests, HMAC helper independent of Stripe.net). Action renamed to ReceiveAsync |
| 2026-10-12 | 1.3 | Done | 0.6 | IEventPublisher in Application/Messaging takes the event ID and the raw JSON string; the publisher never parses. Sealed ServiceBusEventPublisher in Infrastructure: MessageId = evt_ ID, body = original JSON, ContentType application/json, one reused sender. New tests/Payments.IntegrationTests with Testcontainers.ServiceBus (same emulator tag and config.json as compose), excluded from CI by project-name filter until 3.5; CLAUDE.md commands split unit/integration. Code review items for 1.4 (ID validation → 400, SDK retry cap, singleton registration) and 3.5 (shared container fixture) in pending.md; 256 KB item moved to 1.4 |
| 2026-10-12 | 1.4 | Done | 0.7 | Controller reads only `id` via JsonDocument after the signature (non-empty string ≤128, MessageId limits; no evt_ prefix check) → 400 otherwise; publishes the raw body with RequestAborted and answers 200 only after publish; publisher exception → error log with event ID and 500, client abort → warning only. Stripe resends any non-2xx, so 400 vs 500 is about meaning, not retries. ServiceBusOptions validated on start; ServiceBusClient and publisher registered as singletons, retries capped (2 retries, 2 s TryTimeout, ~8 s worst case). 256 KB kept, section 8 wording fixed. New tests/Payments.Api.Tests (11 tests, WebApplicationFactory with fake publishers, HMAC helper linked). Code review: abort handling and blank-id test fixed; compose readiness (1.5), managed identity options (4.6) and cold-start TryTimeout (4.7) moved to pending.md; Utf8JsonReader dropped |
| 2026-10-12 | 1.5 | Done | 0.8 | Stripe CLI 1.53.1 (winget). `listen` now requires an event list: uses `--events` with the 7 subscribed types (section 2), so only `payment_intent.created` and `.succeeded` arrive. Default `trigger` fixture fails on API version `2026-09-30.endive` (`payment_method_types` rejected): documented `--remove`/`--add automatic_payment_methods` variant. Fixtures taken from the queued message bodies (byte-exact raw body except the masked `client_secret` value, so not usable for signature checks) via a throwaway `dotnet run peek.cs`; only the `client_secret` value masked as `redacted`; `.gitattributes` keeps fixtures `-text`. Emulator readiness: image has no shell, `localhost:5300/health` turns 200 the same second publishing works, wait documented in CLAUDE.md; pending item closed |
| 2026-10-12 | 1.6 | Done | 0.5 | Ingestion moved into ReceiveStripeEvent (Application/Webhooks): signature, then id parse, then publish, returning a result enum (Published, InvalidSignature, InvalidEventId, PublishFailed); the use case logs, a caller cancellation is a warning, any other publish failure an error with the event ID. Controller only reads the raw body and maps the result to 200/400/500, with no try/catch and no Infrastructure reference, enforced by ControllerDependencyTests. IWebhookSignatureVerifier moved to Application, parameter renamed to signature. New tests/Payments.Application.Tests (13 tests, log messages checked for event ID and reason); RecordingEventPublisher shared by link; Api assertions unchanged. Layering rule in CLAUDE.md and architecture section 11. Body-read abort moved to pending.md (3.4). M1 now 6 tasks, 5.5 to 8.5 h |
| 2026-10-12 | review | Done | 0.5 | Architecture review fixes. Dead-letter on expiration on for payment-events only (default is off; NFR3), alert scoped to it. Pipeline gets conditioned RBAC Administrator, since Contributor can't write role assignments. what-if moved to deploy.yml, so ci.yml never signs in to Azure (OIDC is main-only). Final states accept no transition; refund on canceled is an impossible conflict. Payments PK violation is a concurrency conflict, InboxEvents PK a duplicate (task 2.6). Decision 3 reason corrected. CLI 1.53.1 verified to refuse listen without --events (error quoted in section 10). Retry is 1 attempt + 3 retries. Five metrics, per-operation pricing, local SQL auth via git-ignored .env, Entra admin creates contained users, migration runs before the image swap, manual transaction span. Three new risks (DLQ receive by ID, pipeline roles, Azure ActivitySource) |
| 2026-10-14 | 2.1 | Done | 0.5 | PaymentStatus and TransitionOutcome as byte enums starting at 1 (tinyint, default never valid). PaymentTransitions.Decide is pure and takes the requested state, not the Stripe type (parser maps charge.refunded in 2.2), plus refunded amounts for partial on partial (larger applied, equal redundant, smaller out of order). Rules: impossible conflict, refund amounts, same state redundant, levels with the requires action/processing switch; levels private. No final-state rule: turning it off kept all 64 cases green, since refunded is the top level and canceled's non-lowering events are conflicts; section 6 wording fixed. Unknown payment left to 2.4. 68 domain tests: literal 64-row table, 2 refund facts, a coverage fact; mutation check fails a row. Glossary terms added to section 1 |
| 2026-10-16 | 2.2 | Done | 0.6 | StripeEventParser (static, Application/Events, JsonDocument) maps the raw event to a PaymentEvent record (event id/type/created, payment intent, requested status, amount, amount refunded, currency); charge.refunded is partial when amount_refunded < amount, read from data.object only. Strict on the fields it reads, ignores the rest and api_version; InvalidStripeEventException for bad JSON, missing fields, charge without payment_intent and unsupported types. New fixtures: requires_action, payment_failed, canceled, full and partial charge.refunded (partial via refunds create --amount 500). No processing fixture: the BR sandbox has no payment method that goes through processing (pending, M2 review); covered by inline JSON. 13 parser tests; mutation check fails the partial row. Glossary: Payment event |
| 2026-10-17 | 2.3 | Done | 0.7 | Payment in Domain (state only, constructor binding); InboxEvent and PaymentHistoryEntry in Infrastructure/Persistence; fluent mapping in PaymentsDbContext. RowVersion is a required shadow property (nullable rowversion would be varbinary(8)). Every DateTimeOffset is UTC datetime2 via a ConfigureConventions converter. Null only for FromStatus (unknown payment), Reason (applied) and TraceId (no Activity before M5). Migrations in Infrastructure, marked generated in .editorconfig; Payments.Migrator is the startup project and the compose `migrator` service, which creates the database and exits 0, idempotent on rerun. EF 10 turns on READ_COMMITTED_SNAPSHOT locally, matching Azure SQL. Pkcs 10.0.13 pinned over the vulnerable 9.0.11 from SqlClient. Six pending items (2.5, 3.1, 4.8, 5.1, two for M2 review) |
| 2026-10-17 | 2.4 | Done | 0.8 | ProcessStripeEvent (Application/Processing) parses the body, then in one transaction reads the payment, decides (Payment.Apply or create unknown in the requested state), inserts the inbox with the outcome, adds payment/history and commits. Inbox after the read because Outcome is NOT NULL and the row is init-only; section 4 steps 6–7 rewritten. One IPaymentStore port; PaymentStore (EF) treats only error 2627 in the inbox SaveChanges as duplicate, so RowVersion and Payments PK still propagate for 2.6. Result is a record (outcome + reason) so the dead-letter gets the parser message or conflict reason; CLAUDE.md rule updated. InboxEvent and PaymentHistoryEntry moved to Application, no model change. Glossary: Duplicate. 9 use case tests with InMemoryPaymentStore on real fixtures, 6 Payment tests; mutation check fails the duplicate test. PaymentStore without a SQL test → pending (3.5) |
| 2026-10-17 | 2.5 | Done | 0.7 | PaymentEventsWorker (BackgroundService) owns a ServiceBusProcessor built in Program.cs: PeekLock, AutoCompleteMessages off, MaxConcurrentCalls 4, prefetch 0, default client retry. One DI scope per message (ProcessStripeEvent, PaymentStore, DbContext scoped); completes Applied/IgnoredOutOfOrder/Redundant/Duplicate, abandons InvalidEvent/Conflict until 3.2 dead-letters them; no try/catch, and a thrown handler is abandoned by the SDK (redelivered in ~2 s, lock is 60 s). Compose: worker env (Development, emulator key, ConnectionStrings__Payments) and depends_on migrator completed; pending item closed. EF logs at Warning in Development. New tests/Payments.Worker.Tests with WorkerDependencyTests. Verified: stripe trigger created the payment as paid, created recorded out of order, queue and DLQ empty (throwaway peek); succeeded and created raced and one hit PK_Payments, recovered by redelivery (2.6 makes it a retry); events resend logged already processed with counts unchanged. user-secrets command added to CLAUDE.md |
| 2026-10-17 | 2.6 | Done | 0.7 | Store returns PaymentStoreWriteResult (Saved, DuplicateEvent, ConcurrencyConflict) from AddInboxEventAsync (was TryAdd, bool) and CommitAsync. One SaveAsync maps DbUpdateConcurrencyException and 2627 on PK_Payments to conflict, 2627 on PK_InboxEvents to duplicate, using key names from the EF model. BeginTransactionAsync clears the change tracker, since FindAsync would return the stale tracked payment. ProcessStripeEvent retries from the read up to 3 times with no wait, then returns ConcurrencyConflict, which the worker abandons. Glossary: Concurrency conflict; section 6 updated. Integration test (Testcontainers.MsSql) with a read barrier: existing and unknown payment, requires_action and succeeded at the same time, both end paid with two history rows and exactly one conflict; with MaxAttempts = 1 both fail. Three pending items updated (3.1, 3.5 ×2) |
| 2026-10-17 | 2.7 | Done | 0.5 | ProcessStripeEventPropertyTests runs the use case on InMemoryPaymentStore (repetition is the inbox's job and the use case creates unknown payments). Five hand-written lifecycles with expected status, refunded amount and LastEventId; inline JSON with one pi_. Every permutation delivered (up to 5040, n! asserted), plus 1000 seeded runs with each event 1 to 3 times, shuffled. Compares status, amounts, currency, LastEventId and inbox/history row counts; timestamps left out. Found that the property fails when requires action and processing tie at the top (last wins, section 6 rule): pinned by a fact, glossary gains "Valid event sequence" and "Resulting state" (the glossary's "final state" means canceled/refunded). Mutation checks: smaller partial refund applied passed at first (the full refund hid it), so a lifecycle with two partials was added; switch off and ignored duplicate fail; >= between levels is an equivalent mutant |
| 2026-10-17 | 2.8 | Done | 0.8 | Console app tools/Simulator (path from section 11) with pay, refund and replay; flood left to 4.7 (pending). Config from user-secrets or env, no file: Stripe:SecretKey (refused unless sk_test_/rk_test_), Stripe:WebhookSecret, ConnectionStrings:Payments. pay: one confirmed 2000-cent brl PaymentIntent per test card (pm_card_visa, chargeDeclined, authenticationRequired) with automatic payment methods and no redirects; a decline's 402 carries the PI and is printed. refund <pi> [cents]: no amount is full. replay reads InboxEvents.Payload through PaymentsDbContext, doubles and shuffles with a printed seed (ReplayPlan), re-signs each delivery at send time (StripeSignature, 300 s tolerance) and posts sequentially. 16 tests in tests/Simulator.Tests; the signature is checked with the API's own verifier. Verified: 4 payments from stripe trigger plus a partial and a full refund (paid->refunded, partially refunded, failed, canceled; 10 inbox events), replay sent 20 deliveries, all 200, worker logged "already processed" 20 times, before/after snapshots identical including RowVersion. pay and refund not run: no test secret key (pending, M2 review). Each duplicate logs two EF fail entries with stack (pending, 5.6) |
| 2026-10-17 | M2 review | Done | 0.3 | Pending review. processing fixture dropped (parser reads only id, amount, currency, the same fields as the other payment_intent events with real fixtures; exception now permanent in StripeEventParser.md). Payload retention job dropped from section 5 (replay reads the stored payloads; test volume is a few MB of 32 GB), moved to "What I would do differently in production". Pkcs override kept: EF Core 10.0.13 brings SqlClient 6.1.7, still on Pkcs 9.0.11; moved to M3 review. pay/refund verification waits for a test secret key |
