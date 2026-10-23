# 0011. Transient retry window that outlasts a free database resume

Status: accepted (task 4.10). Changes the transient retry delays in section 6 of `docs/architecture.md`.

Numbers 0001 to 0010 are reserved for the ten decisions of section 12, which task 6.2 writes in that order.

## Context

The free Azure SQL database pauses after 60 minutes without use and resumes on the next connection. Task 4.8 measured a resume of about 46 s.

A transient error is retried inside the delivery: 1 attempt plus 3 retries. Until this decision the waits were 200 ms, 1 s and 5 s, 6.2 s per delivery. If the four attempts fail, the worker abandons the message, and an abandon releases the lock at once, so the next delivery starts right away. The 60 s lock duration does not stretch the window. After 5 deliveries the message goes to the dead-letter. If every attempt failed at once, a message would survive about 31 s of outage (5 times 6.2 s), less than a resume.

How fast an attempt fails depends on how the database refuses it. The task 4.10 baseline, with the old delays and the cloud database paused, saw each attempt wait the 15 s connection timeout (`SqlException` -2, "post-login phase"). The first delivery then lasted about 55 s, and both events of the payment were applied in it, on the second and third attempts, with nothing in the dead-letter. A paused database may also answer the login with error 40613 ("not currently available"), which comes back without the wait. In that case the old window is the 31 s above.

## Decision

Wait 1 s, 5 s and 15 s before the three retries, keeping 1 attempt plus 3 retries and 5 deliveries. The waits alone add up to 21 s per delivery and 105 s over 5 deliveries, more than twice the measured resume, whether an attempt fails at once or after the connection timeout.

## Alternatives

- **Keep 200 ms, 1 s and 5 s.** It covered the resume in the baseline only because each attempt took 15 s to fail. It relies on how the server refuses the login, which the project doesn't control.
- **Longer lock duration** (the old plan B in section 14). An abandon releases the lock regardless of its duration, so it doesn't add time between deliveries.
- **More retries per delivery** (for example 1 plus 5). Same total with more attempts, more warnings with a stack each (`docs/pending.md`, task 5.6) and a different attempt count in the tests and in the `transient` demo fault.
- **EF Core's `EnableRetryOnFailure`.** Rejected in task 3.1 (`docs/code/ProcessStripeEvent.md`): its own delays and a split retry between EF Core and the use case.
- **Turn off auto-pause.** The free allowance would run out within the month, and then the database pauses until the next month (section 10).
- **Wake the database from the API.** The API doesn't touch the database on the webhook path (section 3).

## Consequences

- A delivery with a database that is really down holds the message longer: the 21 s of waits plus up to four 15 s connection timeouts, about 81 s. That is more than the 60 s lock, which the processor renews while the handler runs (`MaxAutoLockRenewalDuration`, 5 min by default).
- An event that hits a short blip waits 1 s instead of 200 ms before its first retry. Nobody watches that latency: Stripe already got its `200` from the API.
- The `transient` demo fault now applies the event after 1 s and 5 s instead of 200 ms and 1 s.
- A shutdown during a wait cancels it; the message goes back to the queue and is delivered again, as before.
