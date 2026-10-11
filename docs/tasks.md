# Tasks

System design in [architecture.md](architecture.md). Progress is tracked at the end of this file, under "Session log".

The eight roadmap milestones break down into 47 tasks of 1 to 3 hours each, totaling 49 to 78.5 hours without the optional milestone. Each task fits in one session and has a done criterion that is verified by running something.

| Milestone | Tasks | Hours |
| --- | --- | --- |
| M0. Skeleton | 5 | 4.5 to 5.5 |
| M1. Ingestion | 5 | 4.5 to 7 |
| M2. Processing | 8 | 10 to 16 |
| M3. Failures | 5 | 6 to 9 |
| M4. Cloud | 9 | 10 to 17 |
| M5. Dashboard and telemetry | 8 | 9.5 to 17 |
| M6. Documentation | 4 | 4.5 to 7 |
| Total up to the done criterion | 44 | 49 to 78.5 |
| M7. Horizontal scaling (optional) | 3 | 2.5 to 4.5 |

The total came out above the initial estimate of 40 to 60 hours, which was made before the task breakdown. The breakdown is the better reference.

## How to use

- **One task per session.** Ask Claude Code for a short plan before any code, citing the task number and the section of the architecture document.
- **Read the diff.** Once the code is written, explain out loud what each part does.
- **Azure account only in M4.** From M0 to M3 everything runs on the local machine, and the 30-day credit only starts counting when the account is created.
- **Measure M0 and M1.** If together they take more than 12.5 hours, apply the same factor to the rest.

## M0. Skeleton (4.5 to 5.5 h)

| # | Task | Hours | Done when |
| --- | --- | --- | --- |
| 0.1 | Create the solution and the Domain, Application, Infrastructure, Api and Worker projects, with references in the right direction, `.editorconfig` and `.gitignore` | 1 | `dotnet build` passes and Domain references no other project |
| 0.2 | Write the `docker-compose.yml` with SQL Server and the Service Bus emulator, plus the configuration for the `payment-events` and `payment-notifications` queues | 1 to 2 | `docker compose up` starts both and a test console sends and receives a message on a queue |
| 0.3 | Write multi-stage Dockerfiles for the API and the worker and add them to the compose file | 1 | `docker compose up` starts everything and a `GET` on the API responds |
| 0.4 | Create the test project with a trivial test and the `ci.yml` (restore, build, test) | 1 | A pull request is opened and CI goes green |
| 0.5 | Copy the architecture into `docs/architecture.md` and write `CLAUDE.md` with the conventions | 0.5 | Claude Code correctly answers "what is the idempotency rule?" without you pasting anything |

Task 0.2 is the most uncertain one in the milestone. Setting up the emulator for the first time tends to get stuck on details, so don't leave it for the end of a session.

## M1. Ingestion (4.5 to 7 h)

| # | Task | Hours | Done when |
| --- | --- | --- | --- |
| 1.1 | Create `POST /webhooks/stripe`, reading the raw body, with a 256 KB limit, responding `200` for now | 1 | A `curl` with any body returns `200`, and a body above the limit is rejected |
| 1.2 | Validate the signature with the official Stripe library, 5-minute tolerance, `400` if invalid | 1 to 2 | Tests with a valid signature, an invalid one and an old timestamp pass; tampering with one byte of the body makes validation fail |
| 1.3 | Create the `IEventPublisher` port in Application and the Service Bus implementation: `MessageId` equal to the event ID, body equal to the original JSON | 1 to 2 | An integration test publishes and reads the message on the emulator with the same `MessageId` |
| 1.4 | Wire the two together: publish before responding `200`, and `500` if publishing fails | 1 | With `WebApplicationFactory`: a valid signature returns `200` and publishes a message; a failing publisher returns `500` |
| 1.5 | Run end to end with `stripe listen` and `stripe trigger payment_intent.succeeded`, and save the real JSONs in `tests/fixtures` | 0.5 to 1 | The message shows up on the local queue and the fixture files are in the repository |

## M2. Processing (10 to 16 h)

| # | Task | Hours | Done when |
| --- | --- | --- | --- |
| 2.1 | Domain: `PaymentStatus`, levels, transition table and the four outcomes (applied, ignored as out of order, redundant, conflict) | 1 to 2 | Parameterized tests cover every combination of current state and event |
| 2.2 | Parser from Stripe events to an internal type, using the fixtures | 1 | Each fixture becomes the expected internal event; an unknown type raises a permanent error |
| 2.3 | EF Core: entities, mappings, `RowVersion`, first migration and the Migrator project | 1 to 2 | The compose setup applies the migration and the three tables exist |
| 2.4 | `ProcessStripeEvent` use case: inbox, state and history in one transaction, with duplicates detected by the primary key | 2 to 3 | Tests with test doubles pass for duplicate, unknown payment and partial refund |
| 2.5 | Worker: `ServiceBusProcessor` with manual completion after the commit | 1 to 2 | `stripe trigger` changes the payment in the local database and the message leaves the queue |
| 2.6 | Concurrency: retry processing when the `RowVersion` check fails | 1 | Two simultaneous runs on the same payment end with the correct state and two history rows |
| 2.7 | Property test: permutations and repetitions of valid event sequences | 1 to 2 | Every permutation leads to the same final state |
| 2.8 | Simulator, `pay`, `refund` and `replay` modes | 2 to 3 | `replay` mode does not change the final state of any payment |

Tasks 2.1 and 2.7 are the ones that pay off most in interviews. It's worth writing the transition table by hand before asking for the code.

## M3. Failures (6 to 9 h)

| # | Task | Hours | Done when |
| --- | --- | --- | --- |
| 3.1 | Classify errors as transient or permanent; retry with 3 attempts (200 ms, 1 s, 5 s) and abandon the message | 1 to 2 | A test fails twice with a transient error and passes on the third attempt |
| 3.2 | Immediate dead-letter for permanent errors and impossible conflicts, with reason and description | 1 | An unreadable event shows up in the dead-letter with the reason filled in |
| 3.3 | Fault injection via the `demo_fault` metadata, behind the `FaultInjection:Enabled` setting | 1 | A `poison` payment reaches the dead-letter after 5 deliveries, and a `transient` one passes on the third |
| 3.4 | API: `GET /api/dead-letters`, without removing the messages, and a reprocess `POST`, with `X-Admin-Key` | 1 to 2 | The reprocessed message goes back to the main queue and disappears from the dead-letter; without the key, the API returns `401` |
| 3.5 | Integration tests with Testcontainers: duplicate delivery, poison message and crash after commit | 2 to 3 | The three scenarios pass in CI |

At the end of M3 the system is complete on the local machine. It's a good point to rehearse the answer to "what happens if the worker crashes in the middle of processing?".

## M4. Cloud (10 to 17 h)

| # | Task | Hours | Done when |
| --- | --- | --- | --- |
| 4.1 | Create the Azure account: MFA, a US$ 5 budget with alerts, resource group and region | 0.5 to 1 | The budget alert is active before any resource exists |
| 4.2 | Bicep: managed identities, Service Bus Basic with both queues and per-queue roles | 1 to 2 | `what-if` comes out clean and the deploy creates the namespace and the queues |
| 4.3 | Bicep: free SQL database with an Entra ID admin; create the contained users for the identities | 1 to 2 | Passwordless connection works with `az login`, and the option to pause when the free allowance runs out is on |
| 4.4 | Bicep: Key Vault, Log Analytics with a daily cap, and Application Insights | 1 | The secrets are stored and the 0.2 GB cap is configured |
| 4.5 | Bicep: Container Apps environment, API, worker and migration job, with images from GHCR and secrets referenced from Key Vault | 2 to 3 | `/health/live` responds at the public address |
| 4.6 | Replace connection strings with managed identity for Service Bus and SQL, through configuration only | 1 to 2 | No service secret appears in the container variables, and the worker writes to the database |
| 4.7 | Queue-length scale rule and liveness and readiness probes | 1 to 2 | With the queue empty the worker goes to zero, and the simulator's `flood` mode scales replicas up |
| 4.8 | `deploy.yml` with OIDC: images, Bicep, migration job and new revision | 2 to 3 | A push to `main` publishes a revision tagged with the commit, with no secret stored in GitHub |
| 4.9 | Register the endpoint in the Stripe sandbox and store the `whsec_` in Key Vault | 0.5 to 1 | A test payment created in Stripe shows up in the cloud database |

This is the least predictable milestone, because it depends on trial and error on the real account. Tasks 4.6 and 4.7 validate two risks listed in the document: the role required by the KEDA rule and cold start.

To make the most of the 30-day credit, concentrate M4 and M5 in the weeks right after task 4.1.

## M5. Dashboard and telemetry (9.5 to 17 h)

| # | Task | Hours | Done when |
| --- | --- | --- | --- |
| 5.1 | Read API: payments with keyset pagination, and detail with history | 1 to 2 | The API tests pass and pagination does not use `OFFSET` |
| 5.2 | Notification queue: the worker publishes after the commit, with a 60 s expiry, and the API consumes | 1 to 2 | A processed event produces a notification that the API receives |
| 5.3 | SignalR hub with the `PaymentUpdated`, `EventProcessed` and `QueueStats` messages | 1 to 2 | A test client connected to the hub receives all three messages |
| 5.4 | Dashboard: skeleton, payments screen and detail with timeline | 2 to 3 | The timeline shows applied and ignored events |
| 5.5 | Dashboard: live feed, reconnect with reload, and dead-letter screen | 2 to 3 | A test payment shows up on screen in under 3 s |
| 5.6 | OpenTelemetry and Serilog in both containers, with the Azure Monitor exporter | 1 to 2 | The trace of an event shows the six spans in Application Insights |
| 5.7 | Custom metrics and the two alerts | 1 to 2 | A `poison` payment triggers the dead-letter alert email |
| 5.8 | Check whether an open SignalR connection keeps the API replica from scaling to zero | 0.5 to 1 | The result is recorded in ADR 9 |

At the end of M5 the technical done criterion is met: the system runs in the cloud, with tests, dashboard and telemetry.

## M6. Documentation (4.5 to 7 h)

| # | Task | Hours | Done when |
| --- | --- | --- | --- |
| 6.1 | README in English: what the system does, Mermaid diagram and how to run it locally | 1 to 2 | Someone who has never seen the project can bring the environment up by following the README |
| 6.2 | The ten ADRs, in English | 2 to 3 | Each ADR has context, decision, alternatives and consequences |
| 6.3 | A "what I would do differently" section and a short video or GIF of the demo | 1 | The README shows duplicates, out-of-order events and dead-letter happening |
| 6.4 | A recorded 3-minute rehearsal in English | 0.5 to 1 | You explain each piece and the worker crash without looking anything up |

Write the README and the ADRs yourself, and use Claude Code only to review. That is the written-English practice the plan calls for.

## M7. Horizontal scaling, optional (2.5 to 4.5 h)

| # | Task | Hours | Done when |
| --- | --- | --- | --- |
| 7.1 | Azure SignalR Service on the free tier, via Bicep, and `AddAzureSignalR` in the API | 1 to 2 | The dashboard connects through the service |
| 7.2 | API with 1 to 2 replicas | 1 to 2 | Two browsers on different replicas receive the same update |
| 7.3 | Update ADR 9 with the change and the cost of the minimum replica | 0.5 | The ADR records the before, the after and the reason |

## Session log

At the end of each session, add a row. It's what the next session reads to know where the project stands.

| Date | Task | Status | Actual hours | Notes |
| --- | --- | --- | --- | --- |
| 2026-10-11 | 0.1 | Done | 0.5 | Solution with 5 layered projects; Directory.Build.props centralizes net10.0, nullable, warnings as errors |
| 2026-10-11 | 0.2 | Done | 0.5 | Compose with SQL Server 2022 (shared by emulator and app) + SB emulator 2.0.1. Local TTL 1 h (emulator cap). Emulator on host port 5673 because of a local RabbitMQ on 5672 |
| 2026-10-11 | 0.3 | Done | 0.3 | Multi-stage Dockerfiles for API and worker, wired into compose; GET on the API responds |
| 2026-10-11 | 0.4 | Done | 0.3 | tests/Payments.Domain.Tests with a test guarding Domain's dependencies; ci.yml (restore, build, test) on PRs. PR #1 green. Integration tests and Bicep join CI in 3.5 and M4 |
