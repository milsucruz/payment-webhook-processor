# RequireAdminKeyAttribute

`src/Payments.Api/Authorization/RequireAdminKeyAttribute.cs`, with `AdminOptions.cs` in the same folder.

Requires the `X-Admin-Key` header on a controller action (architecture section 8: "actions that change anything require the `X-Admin-Key` header"). Created in task 3.4 for the reprocess endpoint. This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## How it works

The attribute is a `TypeFilterAttribute` that creates the private `AdminKeyFilter`, an `IAuthorizationFilter`. MVC runs authorization filters before model binding and before the action, so a request without the key never reaches the use case. A wrong or missing key sets `401 Unauthorized` as the result.

`TypeFilterAttribute` lets the filter receive `IOptions<AdminOptions>` from DI while the attribute itself has no constructor parameters.

## Why a filter and not an authentication scheme

ASP.NET Core authentication (`AddAuthentication` with a custom `AuthenticationHandler` and `[Authorize]`) is the framework's general answer and would fit a future Entra ID login. For one shared key on one action, it adds a handler, a scheme name and a policy to do what ten lines of filter do. Section 8 already names Entra ID as the next step; the filter would be replaced then, not extended.

## Why `401` for both a missing and a wrong key

`403` means "I know who you are and you can't do this". A key either matches or proves nothing, so there is no identity to forbid. Both cases are `401`.

## Constant-time comparison

Both the configured key and the header are hashed with SHA-256, and `CryptographicOperations.FixedTimeEquals` compares the two hashes without stopping at the first difference, so the response time doesn't reveal how many leading characters were right. Hashing first makes both sides 32 bytes: `FixedTimeEquals` returns at once when the lengths differ, which on the raw keys would reveal the key length (found in code review). The hash is not for storage; the key is still read from configuration.

No trimming: `"key "` is a different key (covered by a test).

## Configuration

`Admin:ApiKey`, validated on start: the API refuses to start without it, like `Stripe:WebhookSecret`. An empty key would otherwise match an empty header. Locally it comes from `ADMIN_API_KEY` in `.env` through `docker-compose.yml`; in the cloud, from the `admin-api-key` Key Vault secret (section 8), wired in task 4.5.
