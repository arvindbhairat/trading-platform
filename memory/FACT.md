## BSON Null-safe field accessors

`BsonNull.Value` is NOT C# null — it is a `BsonValue` instance. Using `?.AsString`, `?.AsBsonDocument`, or any `.AsXxx` accessor on `BsonNull.Value` throws `InvalidCastException`.

**Solution**: Use the extension methods in `SignalStack.Storage.BsonExtensions`:

- `doc.GetBsonStringOrNull("field")` — returns `string?` (formerly `doc.GetValue("field", BsonNull.Value)?.AsString`)
- `doc.GetBsonDocumentOrNull("field")` — returns `Dictionary<string, object?>?` (formerly `doc.GetValue("field", BsonNull.Value)?.AsBsonDocument?.ToDictionary()`)
- `doc.GetBsonDateTimeOrNull("field")` — returns `DateTime?` (formerly `doc.GetValue("field", BsonNull.Value)?.ToNullableUniversalTime()`)
- `doc.GetBsonInt32OrNull("field")` — returns `int?` (formerly `doc.GetValue("field", BsonNull.Value)?.AsInt32`)

The `?.` operator on a nullable *document* (`nullableDoc?.GetBsonStringOrNull("field")`) is still fine — that handles C# null document references, not BSON null field values.

## FYERS Auth Header Format

FYERS v3 uses a non-standard Authorization header format: `Authorization: {AppID}:{access_token}` — a single header value with NO space between AppID and the colon.

`AuthenticationHeaderValue` CANNOT be used for this format:
- The two-arg constructor `new AuthenticationHeaderValue(appId, $":{token}")` inserts a space: `Authorization: {appId} :{token}` — FYERS rejects with HTTP 422.
- The single-arg constructor `new AuthenticationHeaderValue($"{appId}:{token}")` validates the value as an HTTP token, and `:` is NOT a valid token character (RFC 7230) — throws `System.FormatException`.

**Always use**: `request.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{token}")` — bypasses the token validation and produces exactly `Authorization: AppID:token` with no extra space.

## FYERS API Error Telemetry Tags

When a FYERS API call fails in `SendAndParseAsync`, the current OTEL span is tagged with:
- `fyers.symbol` — the FYERS-format symbol
- `fyers.endpoint` — the FYERS endpoint path
- `fyers.status_code` — the HTTP status code
- `fyers.response_body` — the raw response body (for 4xx/5xx diagnostics)

This lets you `breakdowns: ["fyers.symbol"]` in Honeycomb queries to find which symbols are failing.

## Worker Singleton

The worker uses a Redis-based singleton lease (`SET NX PEXIRE`) to ensure only one instance runs. Lease TTL = 60s, refresh every 20s. On conflict during startup, it retries with exponential backoff (2^attempt, capped at 64s). Max 8 retries (~4 min window) to handle Railway rolling-deploy overlaps.
