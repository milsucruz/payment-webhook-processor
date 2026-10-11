# Pending items

Open questions found during a task that are out of its scope. Review this file at the end of every milestone: resolve each item, move it to a task, or drop it with a reason.

| Found in | Item | Resolve by |
| --- | --- | --- |
| 1.1 | The webhook body limit is 256 KB (section 8), but the Service Bus Basic 256 KB cap counts the body **and** the message headers. A body just under 256 KB passes the API and then fails to publish, so the API answers `500` and Stripe retries until it gives up. Decide whether to lower the limit (and fix section 8) or accept and document the edge case. Real `payment_intent` events are a few KB. | 1.3 or 1.4 |
| 1.1 | `StripeWebhooksController.Receive` is async but has no `Async` suffix, which the naming rule in `CLAUDE.md` now requires. Rename it to `ReceiveAsync`. | 1.2 |
