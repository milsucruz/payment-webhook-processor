# Dashboard

`web/` (the code), served by `src/Payments.Api` (`Payments.Api.csproj` links the files into `wwwroot`; `Program.cs` maps them).

The single-page dashboard of section 7. Task 5.4 built the skeleton, the payments screen and the payment detail with the timeline; the live feed and the dead-letter screen come in task 5.5. This document holds the detail that the code leaves out. Keep it in sync when the dashboard changes.

| File | Role |
| --- | --- |
| `index.html` | Page shell: top bar with the navigation, and the `#view` element each screen renders into |
| `app.js` | Hash router: `#/payments` (default) and `#/payments/{paymentIntentId}` |
| `api.js` | `fetch` calls to the read API (`docs/code/PaymentsController.md`); `404` becomes `null`, any other failure throws |
| `format.js` | Status labels and the status badge, amounts in minor units to currency text, dates in the browser's locale |
| `payments.js` | Payments screen: table and "Load more" |
| `payment-detail.js` | Payment detail: summary and timeline |
| `dom.js` | `el(tag, properties, ...children)`, the one helper that builds elements |
| `styles.css` | Styles |

## Plain JavaScript, no build step

ES modules loaded straight by the browser, with no framework, bundler or Node in the build. Four screens of tables and lists don't need a component framework, and leaving out a build keeps Node out of the API's Dockerfile and out of CI. The cost is building the DOM by hand, which `el` keeps short. If the screens grow past that (task 5.5 adds two, with live updates), a framework can come in then with its build; nothing here has to be undone first.

## Served from `web/` through `wwwroot`

Section 11 lists `web/` for the dashboard code and the API project for the dashboard files. With no build step the code is the files, so `Payments.Api.csproj` links `web/**` into `wwwroot` with a `Content` item instead of copying them. The static web assets SDK picks them up as the project's assets, publishes them with the API and generates the endpoints manifest that `MapStaticAssets` reads. The Dockerfile copies `web/` next to `src/` for that reason.

The `Content` item sets `ContentRoot` to `web/`. Without it the SDK records discovered assets under `src/Payments.Api/wwwroot/`, a folder that doesn't exist, and in `Development` (`dotnet run` and `WebApplicationFactory`) the host fails at startup with `DirectoryNotFoundException` when it loads the development manifest. Published output doesn't use that manifest, so the container worked either way; the API tests caught it.

## `MapStaticAssets` and `UseDefaultFiles`

`MapStaticAssets` (rather than `UseStaticFiles`) answers with `Cache-Control: no-cache` and a content-hash `ETag` for files requested by their plain name. The browser revalidates on every load and gets a `304` until the file changes, so a deploy never leaves it running an old `app.js`. `UseStaticFiles` sends only `Last-Modified` and lets the browser guess a freshness time. It also serves precompressed `.gz` copies.

`UseDefaultFiles` runs before `UseRouting` and rewrites `/` to `/index.html`, which `MapStaticAssets` then matches as an endpoint.

## Hash routing

Routes live after `#`, so the server only ever sees `/` and needs no fallback route sending unknown paths to `index.html`. Such a fallback would have to be kept away from `/api`, `/hubs` and `/health`; with hash routing there is nothing to keep away. The cost is URLs like `/#/payments/pi_...`, fine for an operator's dashboard.

A payment ID in a link is percent-encoded, and the router decodes it. A malformed escape (a mangled pasted link) makes `decodeURIComponent` throw, so the router catches it and shows "Page not found." instead of a blank page.

Each navigation renders into a new container that replaces the old one. A slow response for the screen the user left then fills a detached element and never shows up on the current screen.

## Payments screen

The read API pages by keyset (`nextCursor`), which can only move forward. So the screen has "Load more", which appends the next page, instead of numbered pages. The button hides when `nextCursor` is `null`. The page size is the API default (50).

A failed load shows the error and turns the button into "Retry", which repeats the same request (the cursor only advances on success). In the cloud the first load can fail while the free database resumes, and without the button the only way out would be reloading the page (code review).

Amounts arrive in minor units (cents). `money` reads how many decimals the currency has from `Intl.NumberFormat` (2 for BRL and USD, 0 for JPY) and divides by that power of ten, so a zero-decimal currency doesn't come out a hundred times too small. Stripe has a few currencies whose minor unit differs from ISO 4217 (ISK and UGX are sent with two decimals that ISO doesn't have), and those would show 100 times too large; test-mode payments here are USD, so the code review's suggestion of a Stripe exception table was not taken. A new formatter is built per amount; caching one per currency would save work nobody notices at 50 rows.

## Timeline

The history comes in write order (`docs/code/PaymentReader.md`), which is processing order, not Stripe's `created` order (section 6). Each entry shows Applied or "Ignored: <reason>" (just "Ignored" if the nullable reason is empty) with the reason as the worker records it (`out of order`, `redundant`; `docs/code/ProcessStripeEvent.md`), the event type, the transition (`New payment` when `fromStatus` is `null`, the first event of an unknown payment), the time and the event ID. An ignored entry is the out-of-order delivery the demo wants to show (section 5, "History of ignored events").

Section 7 also asks for a link to the trace. The trace ID is always `null` until the worker has tracing, and the link needs the Application Insights address, so it waits for task 5.6 (`docs/pending.md`).

## Text only through `textContent`

`el` assigns properties and appends children as text nodes; nothing is set through `innerHTML`. IDs, event types and reasons come from Stripe payloads and the database, so building markup from them would open the page to injected HTML.

## Tests

`tests/Payments.Api.Tests/Dashboard/DashboardFilesTests.cs`: `/` serves `index.html` (so `UseDefaultFiles` and `MapStaticAssets` work together and the linked files reach the host), and every module and the stylesheet come with their media type, `no-cache` and an `ETag` (a module missing from the build would break every import, so all of them are listed). The screens themselves have no automated tests; task 5.4 checked them in a browser against compose: the detail of a payment with an ignored out-of-order `payment_intent.created` between two applied events, and "Load more" taking 69 payments to two pages with no repeats and hiding on the last.
