# Dashboard

`web/` (the code), served by `src/Payments.Api` (`Payments.Api.csproj` links the files into `wwwroot`; `Program.cs` maps them).

The single-page dashboard of section 7. Task 5.4 built the skeleton, the payments screen and the payment detail with the timeline; task 5.5 added the hub connection, the live feed, live updates on the other screens, the reload on reconnect and the dead-letter screen; task 5.9 closes the connection while the tab is hidden. This document holds the detail that the code leaves out. Keep it in sync when the dashboard changes.

| File | Role |
| --- | --- |
| `index.html` | Page shell: top bar with the navigation and the connection status, and the `#view` element each screen renders into |
| `app.js` | Hash router: `#/live` (default), `#/payments`, `#/payments/{paymentIntentId}` and `#/dead-letters`; starts the feed and the connection |
| `live.js` | The one hub connection: start and reconnect, pause while the tab is hidden, status, `onHub` and `onReconnected` subscriptions |
| `live-feed.js` | Live screen, and the feed and counters it shows, collected for the whole page |
| `api.js` | `fetch` calls to the read API (`docs/code/PaymentsController.md`) and the dead-letter API (`docs/code/DeadLettersController.md`); `404` on a read becomes `null`, any other failure throws |
| `format.js` | Status labels and the status badge, amounts in minor units to currency text, dates in the browser's locale |
| `payments.js` | Payments screen: table, "Load more" and pushed payments |
| `payment-detail.js` | Payment detail: summary and timeline |
| `dead-letters.js` | Dead-letter screen: table, admin key and reprocess |
| `dom.js` | `el(tag, properties, ...children)`, the one helper that builds elements |
| `lib/signalr.min.js` | The SignalR JavaScript client, `@microsoft/signalr` 10.0.11 (MIT), unchanged from `dist/browser` of the npm package |
| `styles.css` | Styles |

## Plain JavaScript, no build step

ES modules loaded straight by the browser, with no framework, bundler or Node in the build. Four screens of tables and lists don't need a component framework, and leaving out a build keeps Node out of the API's Dockerfile and out of CI. The cost is building the DOM by hand, which `el` keeps short. Task 5.5 added two screens and live updates and the hand-built DOM still held: each screen is one function with a `load` and a few hub handlers.

### The SignalR client is a copied file

The client comes from npm, and without a build there is nothing to install it. Two options: a `<script>` from a CDN, or the file in the repository. The file won: the dashboard then depends on nothing outside the API at run time, works in compose with no internet, and is served like the other files (`MapStaticAssets`, `ETag`). The cost is about 47 KB in the repository and updating it by hand. It is the UMD build, loaded with a classic `<script defer>` before the `app.js` module, and it exposes `window.signalR`; deferred classic scripts and modules run in document order, so `live.js` finds it. The file is kept byte for byte as published (SHA-256 `97e9b97e642a72e5a470917147a2bf79f86cad829a5c6786adb10614d248bb95`) so it can be checked against the package; that is also why its last line still points at a `.map` file that isn't copied, which only DevTools requests. `.gitattributes` marks `web/lib/**` as `-text`, like the Stripe fixtures: with `core.autocrlf` on, a Windows checkout would otherwise turn its newline into CRLF and the hash would no longer match.

To update: `npm pack @microsoft/signalr@<version>` in a scratch folder, copy `package/dist/browser/signalr.min.js` over `web/lib/signalr.min.js`, and update the version and hash here. The client's stateful reconnect stays off (`docs/code/PaymentsHub.md`).

## Served from `web/` through `wwwroot`

Section 11 lists `web/` for the dashboard code and the API project for the dashboard files. With no build step the code is the files, so `Payments.Api.csproj` links `web/**` into `wwwroot` with a `Content` item instead of copying them. The static web assets SDK picks them up as the project's assets, publishes them with the API and generates the endpoints manifest that `MapStaticAssets` reads. The Dockerfile copies `web/` next to `src/` for that reason. The `**` takes `lib/` too.

The `Content` item sets `ContentRoot` to `web/`. Without it the SDK records discovered assets under `src/Payments.Api/wwwroot/`, a folder that doesn't exist, and in `Development` (`dotnet run` and `WebApplicationFactory`) the host fails at startup with `DirectoryNotFoundException` when it loads the development manifest. Published output doesn't use that manifest, so the container worked either way; the API tests caught it.

## `MapStaticAssets` and `UseDefaultFiles`

`MapStaticAssets` (rather than `UseStaticFiles`) answers with `Cache-Control: no-cache` and a content-hash `ETag` for files requested by their plain name. The browser revalidates on every load and gets a `304` until the file changes, so a deploy never leaves it running an old `app.js`. `UseStaticFiles` sends only `Last-Modified` and lets the browser guess a freshness time. It also serves precompressed `.gz` copies.

`UseDefaultFiles` runs before `UseRouting` and rewrites `/` to `/index.html`, which `MapStaticAssets` then matches as an endpoint.

## Hash routing

Routes live after `#`, so the server only ever sees `/` and needs no fallback route sending unknown paths to `index.html`. Such a fallback would have to be kept away from `/api`, `/hubs` and `/health`; with hash routing there is nothing to keep away. The cost is URLs like `/#/payments/pi_...`, fine for an operator's dashboard.

A payment ID in a link is percent-encoded, and the router decodes it. A malformed escape (a mangled pasted link) makes `decodeURIComponent` throw, so the router catches it and shows "Page not found." instead of a blank page.

Each navigation renders into a new container that replaces the old one. A slow response for the screen the user left then fills a detached element and never shows up on the current screen. Each navigation also gets an `AbortController`: the router aborts the previous one, and every hub handler a screen registered with that signal is removed, so a screen the user left stops reacting to messages and reloads.

## The hub connection (`live.js`)

One connection for the whole page, to `/hubs/payments`, started once by `app.js` after the first screen began its REST load (section 7: "fetches state via REST and then connects to the hub"). Screens don't open their own: the feed needs to count events while another screen is shown, and one connection is one `QueueStats` stream. Screens subscribe with `onHub(method, handler, signal)` and `onReconnected(handler, signal)`.

**Reconnect that never gives up.** `withAutomaticReconnect()` with no arguments tries after 0, 2, 10 and 30 s and then closes for good, leaving a dashboard that looks fine and never updates. The policy here waits 0, 2 and 10 s and then every 30 s, with no end. The first `start()` is not covered by automatic reconnect at all (a cold API in the cloud refuses it), so `start` loops with the same delays. `onclose`, which with this policy only fires when the server closes the connection without asking for a reconnect, starts over too.

**Status.** The top bar shows "Connecting…", "Live", "Reconnecting…" or "Paused" (below), so a dashboard that stopped updating says so.

**Reload on reconnect.** Hub messages are not the source of truth (section 7): whatever was sent while the connection was down is lost. So every connection that follows a gap (a reconnect, a restart after `onclose`, or a first start that needed more than one attempt) calls the `onReconnected` handlers, and each screen redoes its REST load. A first start that worked at once doesn't, because the screen's load just ran. Restarting the API in compose with the page open (task 5.5) showed two reconnects about 15 s apart, each with its reload: the old container, still shutting down gracefully, took the first one, and the new container the second.

**What a reconnect can't recover.** The live feed has no REST source: there is no endpoint for recent events, and adding one would be one more read of the free database for each reconnect. A reconnect puts a line in the feed instead ("Reconnected: events processed while disconnected are not shown here.").

**Pause while the tab is hidden (task 5.9).** An open connection keeps the API replica up, and the replica only scales to zero about 5 minutes after it closes (task 5.8, section 7). So a tab forgotten in the background would hold a 0.25 vCPU replica, and keep the API peeking `payment-events` every 5 s, for as long as it stays open. `live.js` listens to `visibilitychange`: 60 s after the tab is hidden it stops the connection and shows "Paused"; when the tab is shown again it starts the connection as after a gap, so every screen redoes its REST load and the feed gets its "Reconnected" line. Whatever happened while paused is then on screen, from the source of truth.

- **`visibilitychange`, not `blur`/`focus`.** `blur` fires when another window takes the focus while the dashboard is still on screen, for example a terminal next to it during a demo. `visibilitychange` fires only when the page stops being visible: another tab, a minimized window. `pagehide` only fires when the page is unloaded.
- **60 s of grace.** Closing at once would make every quick look at another tab cost a reconnect, a REST reload (a read of the free database, which may wake it) and a "Reconnected" line. A hidden tab that comes back within 60 s keeps its connection and loses nothing. The cost is one more minute of replica, next to the 5 minutes of cooldown. Chrome only throttles timers of hidden tabs hard after 5 minutes hidden, so the 60 s timer fires on time.
- **A page opened hidden** (a link opened in a background tab) schedules the pause too: `startConnection` runs the visibility handler once after the first start.
- **The pause can land at any point of a connection's life**, and a counter (`run`, the `generation` idea of the screens) keeps it simple. Every `start` takes a number and every pause bumps it; a `start` loop that wakes up from its retry delay with an old number stops. Without it, a tab shown again while an old loop slept would run two loops: the second `connection.start()` fails with "not in the 'Disconnected' state" and, since the loop never gives up, would retry every 30 s for as long as the page lives. A pause during the client's own automatic reconnect cancels it (`stop()` ends the reconnect and fires `onclose`), and `onclose` doesn't start over while paused. A pause during `connection.start()` makes it reject, and the loop stops on the old number. Showing the tab waits for the `stop()` in flight and only then clears `paused` and starts: the client runs the `onclose` handlers inside `stop()`, before its promise resolves, so clearing `paused` first would let that `onclose` start a second loop (code review). After the wait it checks that the tab is still visible and still paused, since the tab may have been hidden again, or an earlier show may have started already. So the two guards split the work: `paused` decides whether `onclose` starts over, and `run` decides whether an old loop carries on. A `stop()` that rejects is ignored, so it can't leave the tab "Paused" for good.
- **Not tested: a browser that freezes background pages** before 60 s (phones, Chrome's tab freezing). Then the timer doesn't fire, but the frozen page stops answering the server's keep-alive, the server drops the connection after its 30 s client timeout, and the replica is freed all the same; on return the client's reconnect reloads the screen.
- **The live counters lose the paused time.** They count the events received while connected, which already excluded any connection gap; a pause is one more gap, marked by the "Reconnected" line.

## Live screen

The feed shows `EventProcessed` messages, newest first, up to 100: the browser's time, the outcome (Applied, "Ignored: out of order", Redundant, Duplicate), the event type, a link to the payment and the event ID. The counters (events, applied, out of order, redundant, duplicates; section 7 asks for duplicates and out of order) count since the page was opened, leaving out what arrived while the connection was down or paused.

The feed and counters live in `live-feed.js` at module level and its handlers are registered once by `startLiveFeed`, before the connection starts, not by the screen. So they keep counting while another screen is shown, and coming back to Live shows them; reloading the page starts them over. The screen re-renders the whole list on each change, which at 100 entries costs nothing noticeable and needs no diffing; even a `flood` (8 payments a second, two or three events each) rebuilds a few thousand small elements a second, which a browser does without effort (code review raised it; kept for the simpler code).

The queue line shows the last `QueueStats` (waiting and dead-lettered messages of `payment-events`), with "1000+" when a count reaches `countLimit` (`docs/code/ServiceBusQueueStatsReader.md`). Until the first message it says "waiting for the first count…": the broadcaster sends every 5 s, so that lasts at most 5 s, and task 5.5 decided not to send the stats on connect (`docs/pending.md`, the 5.3 item, dropped).

## Payments screen

The read API pages by keyset (`nextCursor`), which can only move forward. So the screen has "Load more", which appends the next page, instead of numbered pages. The button hides when `nextCursor` is `null`. The page size is the API default (50).

A failed load shows the error and turns the button into "Retry", which repeats the same request (the cursor only advances on success). In the cloud the first load can fail while the free database resumes, and without the button the only way out would be reloading the page (code review).

**Pushed payments.** `PaymentUpdated` carries the same shape as a list item, so the screen draws it with the same row function. The screen keeps a map from payment ID to its row and `updatedAt`:

- A pushed payment goes to the top (the list is newest first) and replaces its old row wherever it was.
- A row from a REST page is appended, or replaces the existing row in place.
- Either way, a payment older than the row already shown is dropped. That covers both races: a push that arrives before the page that contains the same payment, and a "Load more" page that holds a payment already pushed to the top, which would otherwise show twice.

A payment updated while only its older position was in a page not yet loaded moves ahead of the cursor, so it only shows up through its push. If that notification was lost (the worker only logs a failed publish, `docs/code/ProcessStripeEvent.md`), the payment is missing from the list until the next reload or reconnect: hub messages are not the source of truth, and the REST load is (code review, accepted).

The comparison uses `Date.parse`, which keeps milliseconds; two versions in the same millisecond count as equal and the later one wins, and both carry the same committed state in practice.

**Reload.** On reconnect the screen clears the rows, the map and the cursor and loads the first page. A counter of loads (`generation`) makes a page that was in flight before the reload drop its result, so it can't append old rows after the new first page.

Amounts arrive in minor units (cents). `money` reads how many decimals the currency has from `Intl.NumberFormat` (2 for BRL and USD, 0 for JPY) and divides by that power of ten, so a zero-decimal currency doesn't come out a hundred times too small. Stripe has a few currencies whose minor unit differs from ISO 4217 (ISK and UGX are sent with two decimals that ISO doesn't have), and those would show 100 times too large; test-mode payments here are USD, so the code review's suggestion of a Stripe exception table was not taken. A new formatter is built per amount; caching one per currency would save work nobody notices at 50 rows.

## Payment detail and timeline

The history comes in write order (`docs/code/PaymentReader.md`), which is processing order, not Stripe's `created` order (section 6). Each entry shows Applied or "Ignored: <reason>" (just "Ignored" if the nullable reason is empty) with the reason as the worker records it (`out of order`, `redundant`; `docs/code/ProcessStripeEvent.md`), the event type, the transition (`New payment` when `fromStatus` is `null`, the first event of an unknown payment), the time and the event ID. An ignored entry is the out-of-order delivery the demo wants to show (section 5, "History of ignored events").

The detail reloads over REST when an `EventProcessed` for its payment arrives, and on reconnect. The message doesn't carry the new history entry, so a reload is the only way to show it; a `Duplicate` writes nothing, so it doesn't reload. A `generation` counter drops an older response that arrives after a newer one.

Since task 5.6 each entry also shows its trace ID, the operation ID to search in Application Insights. It is not a link: a portal link needs the Application Insights resource in the page and a portal URL format that Microsoft does not document, so a change on their side would break it silently. The M5 review changed section 7 to ask for the trace ID.

## Dead-letter screen

A table of `GET /api/dead-letters`: enqueue time (when the message entered the queue, not when it was dead-lettered, which Service Bus doesn't expose on the message), event ID, reason, description, last failure, deliveries and a Reprocess button per row.

**Last failure.** `MaxDeliveryCountExceeded` alone doesn't say why 5 deliveries failed. Since task 5.5 the worker leaves the use case's reason on the message when it abandons after a transient error or a concurrency conflict (`docs/code/PaymentEventsWorker.md`), so a database that stayed down shows its reason there, and a poison message shows nothing in that column. The value starts with the delivery it came from (`Delivery 5: ...`); a lower number means the later deliveries failed with an exception that left no reason.

**Admin key.** Reprocess needs `X-Admin-Key` (section 8). The key is typed in a password field and kept in a module variable: it survives moving between screens and is gone when the page reloads. It is not stored in `localStorage`, where it would outlive the tab and be readable by any script on the origin. The responses: `202` says the event is back in the queue and reloads the list, `401` says "Wrong admin key.", `404` says the event is no longer in the dead-letter and reloads (another reprocess took it, or a concurrent one holds its lock, `docs/code/ServiceBusDeadLetterQueue.md`). No confirmation dialog: a reprocess is safe to repeat, because the inbox turns an event already applied into a duplicate.

**Refresh.** The list reloads when the dead-lettered count in `QueueStats` changes from the previous message, and on reconnect, and has a Refresh button. Listing only peeks Service Bus and never reaches the database, so following the count costs nothing on the free database. The first `QueueStats` reloads too: a message dead-lettered between the first load and that message would otherwise be missed (code review). A count stuck at `countLimit` (1000) stops showing changes; the list shows at most 100 anyway, and Refresh still works. A load that succeeds clears the error of a failed one, but not the message of a reprocess.

A reprocessed poison message comes back after 5 more deliveries with a new sequence number, so the row returns a few seconds later; that is the poison message doing what it does, not the reprocess failing.

## Text only through `textContent`

`el` assigns properties and appends children as text nodes; nothing is set through `innerHTML`. IDs, event types, reasons and dead-letter descriptions come from Stripe payloads, Service Bus and the database, so building markup from them would open the page to injected HTML.

## Tests

`tests/Payments.Api.Tests/Dashboard/DashboardFilesTests.cs`: `/` serves `index.html` (so `UseDefaultFiles` and `MapStaticAssets` work together and the linked files reach the host), and every module, the SignalR client and the stylesheet come with their media type, `no-cache` and an `ETag` (a module missing from the build would break every import, so all of them are listed).

The screens themselves have no automated tests. Task 5.4 checked them in a browser against compose. Task 5.5 checked against compose, with `stripe listen` forwarding:

- **The done criterion.** On the Live screen, a page observer recorded when each feed line appeared. With the worker just restarted, the webhook of a `stripe trigger` arrived in second 16:33:58 UTC (the `stripe listen` log) and the line appeared at 16:34:00.668, so 1.7 to 2.7 s; with the worker warm, the webhook arrived in second 16:34:30 and the line at 16:34:30.598, under 0.6 s. The second payment also showed its `payment_intent.created` as "Ignored: out of order", and the counters read 4 events, 3 applied, 1 out of order.
- **Pushed payment.** On the payments screen with 50 rows, a new payment from a `stripe trigger` went to the top without a reload (an observer saw it at 16:40:10.549 UTC) and the table had 51 rows, the new one once, ending as Paid.
- **Dead-letter.** A `demo_fault=poison` payment appeared on the open screen without a click, as two `MaxDeliveryCountExceeded` rows with 5 deliveries and no last failure. Reprocess with no key said "Enter the admin key to reprocess.", with a wrong key "Wrong admin key.", and with the key from `.env` "Event ... is back in the queue." and the row left the list.
- **Reconnect.** After restarting the API with the page open, the status was back at "Live" and the feed had the two reconnect lines described above. Later, during a few idle minutes with the browser window in the background, the connection dropped once with nothing in the API log and came back by itself: the payments screen had reloaded its first page. Chrome throttles timers in background tabs, which can delay the client's keep-alive past the server's 30 s timeout; that cause is a guess, not verified.
- No errors from the dashboard in the console.

Task 5.9 checked the pause against compose in two ways:

- **Real hidden tab.** A Node script (scratchpad, not versioned) started Edge headless with a temporary profile and the DevTools protocol, opened the payments screen in one tab and activated another tab with `Target.activateTarget`, which makes the dashboard `hidden` exactly as a user switching tabs does. The WebSocket closed 60.04 s after the switch and the status read "Paused"; activating the dashboard again opened a `negotiate`, a new WebSocket and a `GET /api/payments` within 0.1 s, back at "Live" with 50 rows; no console errors.
- **Edge cases**, with `document.visibilityState` overridden in the page and `visibilitychange` dispatched by hand. Hidden for 5 s and shown: still "Live" 71 s later, with no reload. API stopped, tab hidden while "Reconnecting…": "Paused" 60 s later, and with the API started again no `negotiate` for 57 s (the reconnect would have tried twice), then shown: "Live" and the first page reloaded. While paused, the API container had no established connection on port 8080.
- **Shown while the `stop()` is in flight** (the code review race), in the headless Edge, with a script injected before load that wraps the 60 s `setTimeout` so the tab is shown right after `pause()` calls `stop()`. The first version, which cleared `paused` before waiting for the stop, ran two start loops: the hub connected, and the status stayed at "Connecting…" for the 60 s observed. The fixed version: one `negotiate`, "Live", the first page reloaded, and still "Live" 45 s later.
