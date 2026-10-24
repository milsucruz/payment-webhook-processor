# Pending items

Open questions found during a task that are out of its scope. Review this file at the end of every milestone: resolve each item, move it to a task, or drop it with a reason.

| Found in | Item | Resolve by |
| --- | --- | --- |
| 4.7 | Section 10 gives the API an HTTP scale rule of 50 concurrent requests, but `containerapps.bicep` has none, so the platform default (10) applies. With a maximum of 1 replica the threshold changes nothing, so task 4.7 (queue rule and probes) left it out. Add it when the API gets a second replica. | 7.2 |
| 5.1 review | Every API test class repeats the same six `UseSetting` calls (webhook secret, Service Bus connection string and both queues, payments connection string, admin key); tasks 5.1 and 5.2 each had to add one line to four files. Task 5.3 added two more copies (the API and the integration hub tests, the second pointing at the emulator), and task 5.4 one more (`DashboardFilesTests`). A shared test factory with the base settings would keep the next required setting to one place. The M5 review moved it to task 7.1, whose `AddAzureSignalR` is the next required setting: create the factory there, before adding it. | 7.1 |
