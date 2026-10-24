# PaymentCursor

`src/Payments.Application/Reading/PaymentCursor.cs`

The position in the payments list where the next page starts: the `UpdatedAt` and `PaymentIntentId` of the last payment of the previous page (section 5, "Dashboard reads"). This document holds the detail that the code leaves out. Keep it in sync when the class changes.

## Format

`Encode()` writes `<UpdatedAt.UtcTicks>|<PaymentIntentId>` in UTF-8 and encodes it as base64url, without padding. For example, `2026-10-17T18:15:54.6554076Z` and `pi_3URc92KVj7nYvfcn1j90qN6I` become `NjM5Mjc4NTc3NTQ2NTU0MDc2fHBpXzNVUmM5MktWajduWXZmY24xajkwcU42SQ`.

- **Ticks, not an ISO date:** `datetime2` keeps 100 ns, the same unit as a .NET tick, so the value goes to the database and back unchanged. An ISO string in a query string needs care with the `+` of an offset (a `+` in a query string decodes as a space) and with how many fractional digits are written. Losing one digit would make the next page skip or repeat payments that share the second.
- **Base64url:** the value goes in a query string, so it can't contain `+`, `/` or `=`. Being opaque also tells clients not to build it, which keeps the format free to change.
- **`|` as separator:** a ticks value never contains it, and the ID is everything after the first one, so an ID with a `|` would still decode.

The cursor is not signed. Any cursor a client can build only moves the start of a page inside data that is public anyway (section 8), so tampering gains nothing.

## What `TryDecode` rejects

Anything that is not base64url, a missing separator, an empty ticks or ID part, a sign or any non-digit in the ticks (`NumberStyles.None`), and ticks above `DateTimeOffset.MaxValue`, which would make the constructor throw. It returns `false` instead of throwing, so the use case turns a bad cursor into `InvalidCursor` without a `try`/`catch`.

## Tests

`tests/Payments.Application.Tests/Reading/PaymentCursorTests.cs`: a round trip keeps the exact ticks, the encoded value has no `+`, `/` or `=`, and a value that is not base64url plus nine malformed contents are rejected.
