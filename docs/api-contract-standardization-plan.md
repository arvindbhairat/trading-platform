# API Contract Standardization Plan (Backend ↔ Frontend)

## Status

**Date**: 2026-05-28
**Author**: Planning session — Arvind + Copilot
**Status**: Draft — approved phases ready for execution

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Root Cause Analysis](#2-root-cause-analysis)
3. [Surface Area Inventory](#3-surface-area-inventory)
4. [Design Principles](#4-design-principles)
5. [Decision Record](#5-decision-record)
6. [Scope Boundaries](#6-scope-boundaries)
7. [Performance Analysis](#7-performance-analysis)
8. [External Integration Safety Analysis](#8-external-integration-safety-analysis)
9. [Phased Implementation Plan](#9-phased-implementation-plan)
10. [Verification Matrix](#10-verification-matrix)
11. [Relevant Files](#11-relevant-files)
12. [Appendix A: Endpoint Impact Matrix](#appendix-a-endpoint-impact-matrix)
13. [Appendix B: ReadFromJsonAsync Endpoints](#appendix-b-readfromjsonasync-endpoints)

---

## 1. Problem Statement

### 1.1 The Triggering Bug

`POST /api/v1/signals/subscriptions/` fails with `{"error":"Signal type is required."}` for this payload:

```json
{
  "name": "PriceDelta - 5R",
  "signal_type_id": "price_volatility",
  "timeframe": "rolling5",
  "parameters": {},
  "rme_configuration": { ... }
}
```

### 1.2 The Broader Problem

The codebase has **no global JSON serialization configuration**. Naming conventions are fragmented:

| Concern | Convention Used | Enforced? |
|---------|----------------|-----------|
| Frontend request bodies | snake_case (`signal_type_id`) | Manual only |
| Backend request DTOs | PascalCase (`SignalTypeId`) | Not enforced |
| Backend response (MapToDto) | snake_case (explicit `new { signal_type_id = ... }`) | Manual only |
| Backend response (typed records) | PascalCase (`BacktestRunResponse`) | Not enforced |
| Frontend TypeScript DTOs | snake_case (hand-written) | Manual only |

**Result**: Any compound property name (containing `_` in snake_case) fails to deserialize because ASP.NET Core's default case-insensitive matching cannot bridge the underscore gap. `signal_type_id` ≠ `SignalTypeId` even with case-insensitive comparison.

### 1.3 Endpoints Known to Be Affected

| Endpoint | Broken Field | Impact |
|----------|-------------|--------|
| `POST /signals/subscriptions/` | `signal_type_id`, `rme_configuration` | ✅ Confirmed (this bug) |
| `POST /signals/subscriptions/{id}/versions` | `rme_configuration` | ✅ Confirmed |
| `POST /admin/impersonation/start` | `target_user_id` | ⚠️ Likely broken |
| `POST /execution/intent/signed-payload` | `order_type` | ⚠️ Likely broken |
| `POST /backtest/run-from-subscription/{id}` | `date_range_start`, `date_range_end`, `starting_equity` | ⚠️ Likely broken |

---

## 2. Root Cause Analysis

### 2.1 How JSON Deserialization Works in ASP.NET Core

ASP.NET Core minimal API endpoints bind request bodies using `System.Text.Json` with `JsonSerializerDefaults.Web`:

```csharp
// Default Web options:
PropertyNameCaseInsensitive = true
PropertyNamingPolicy = JsonNamingPolicy.CamelCase
```

**Case-insensitive matching** handles simple case differences:
- `name` → `Name` ✅ (same letters, different case)
- `timeframe` → `Timeframe` ✅

But **cannot bridge underscores**:
- `signal_type_id` ↔ `SignalTypeId` ❌ (different characters: `_` vs no `_`)

### 2.2 Two Serialization Paths

The API project uses two different deserialization mechanisms:

**Path A — Endpoint parameter binding** (used by most endpoints):
```csharp
subs.MapPost("/", async (CreateSubscriptionRequest request, ...) => { ... });
```
Uses globally configured `JsonSerializerOptions`. Currently unconfigured, defaults apply.

**Path B — Explicit ReadFromJsonAsync** (used by ~17 endpoints):
```csharp
var body = await context.Request.ReadFromJsonAsync<StartImpersonationRequest>(...);
```
Uses `JsonSerializerDefaults.Web` independently. Not affected by `ConfigureHttpJsonOptions`.

**The fix (global `SnakeCaseLower`) covers Path A. Path B endpoints need a separate review.** However, Path B endpoints are fewer and many use positional records with single-word parameters that already work.

### 2.3 Why contract-diff Did Not Catch This

The `contract-diff` CI step (`packages/testing/src/contract-diff.mjs`) validates only:
1. JSON schema of `*.contract.json` files (are they well-formed?)
2. Live API responses for **GET endpoints only** (POST/PUT/PATCH/DELETE are skipped)
3. No static analysis comparing contract files against C# DTOs or TypeScript interfaces

The contract files correctly document snake_case (`signal_type_id`), but there is **zero enforcement** that the C# DTOs actually accept those field names.

---

## 3. Surface Area Inventory

### 3.1 Backend MapToDto / Transformation Methods

| File | Method | Properties | Has computed fields? |
|------|--------|-----------|---------------------|
| `SignalSubscriptionEndpoints.cs` | `MapToDto` | 13 | Yes (`is_paused`, `has_pending_version`, `version_count`) |
| `AdminAuditEndpoints.cs` | `MapToEntry` | 6 | No |
| `NotificationEndpoints.cs` | `MapToFeedDto` | 14 | Yes (`is_critical`, `delivery_failed`) |
| `AuthEndpoints.cs` | Multiple | ~15 endpoints | Some |

**Key transformations in MapToDto that must be preserved:**
- `ObjectId.ToString()` — MongoDB ID to string
- `DateTime.ToString("o")` — ISO 8601 date formatting
- `BsonNormalizer.NormalizeBsonDocument()` — BSON → plain object
- Computed properties — `is_paused`, `has_pending_version`, `delivery_failed`
- Nested object construction — `current_version` with `version_id`, `version_number`, `effective_from`

### 3.2 Frontend Request Bodies (all snake_case)

| File | Endpoint | Key Fields |
|------|----------|------------|
| `signals/page.tsx:162` | POST `/signals/subscriptions/` | `signal_type_id`, `rme_configuration` |
| `signals/page.tsx:278` | POST `/signals/subscriptions/{id}/versions` | `rme_configuration` |
| `signals/page.tsx:347` | POST `/backtest/run-from-subscription/{id}` | `date_range_start`, `date_range_end`, `starting_equity` |
| `admin/page.tsx:260` | POST `/admin/impersonation/start` | `target_user_id` |
| `Phase1Modal.tsx:292` | POST `/execution/intent/signed-payload` | `order_type` |
| `accept-legal/page.tsx:71` | POST `/auth/accept-legal` | `acceptedTosVersion`, `acceptedPrivacyVersion` (camelCase — works) |

### 3.3 Frontend TypeScript DTOs (all snake_case)

| File | DTO | Fields |
|------|-----|--------|
| `signals/page.tsx:22` | `SubscriptionDto` | `signal_type_id`, `is_paused`, `has_pending_version`, `created_at` |
| `signals/page.tsx:42` | `VersionDto` | `version_id`, `effective_from`, `is_live`, `is_pending` |
| `(user)/page.tsx:22` | `PendingConfirmation` | `submission_timestamp`, `callback_received_at`, `intent_created_at` |
| `(user)/page.tsx:35` | `HoldingDto` | `average_buy_price`, `total_invested`, `current_market_value`, `unrealized_pnl` |
| `notifications/page.tsx:23` | `NotificationDto` | `notification_type`, `type_label`, `is_critical`, `telegram_delivery_status` |
| `admin/` pages | 20+ DTOs | All snake_case |

---

## 4. Design Principles

1. **One source of truth**: C# typed records are the authoritative definition of the API contract. OpenAPI spec and frontend TypeScript types are derived automatically.

2. **Zero per-request overhead**: `JsonNamingPolicy.SnakeCaseLower` caches name mappings once per type at application startup. Per-request cost is a dictionary lookup — identical to `[JsonPropertyName]`. No runtime allocation, no reflection per request.

3. **Systematic over piecemeal**: A single config line in `Program.cs` covers every current and future endpoint. No per-DTO attributes to remember, no code reviews to catch missing `[JsonPropertyName]`.

4. **Backend bears the cost at startup, not per request**: The one-time name mapping cache is paid by the backend. Frontend merely uses the output of `openapi-typescript` at build time.

5. **Incremental adoption**: Each phase is independently verifiable and can be shipped to production. Not all phases need to be done to get value.

6. **No breaking changes to existing functionality**: All phases are designed to preserve the exact same HTTP JSON contract (snake_case) that the frontend already expects.

---

## 5. Decision Record

| Decision | Chosen Option | Rationale |
|----------|--------------|-----------|
| **Naming convention** | snake_case for all HTTP JSON properties | Matches existing frontend convention, `MapToDto` output convention, and TypeScript DTOs. Consistent with what the codebase already does. |
| **Implementation mechanism** | Global `JsonNamingPolicy.SnakeCaseLower` in `ConfigureHttpJsonOptions` | One line of config covers all endpoints. No per-DTO attributes to forget. No code review burden. VS `[JsonPropertyName]` on every property: fragile, verbose, easy to omit. |
| **Response DTO style** | Typed records (eventually replacing anonymous `new { }`) | Enables accurate OpenAPI schema generation. Anonymous types produce `{}` in OpenAPI — useless for codegen and documentation. |
| **MapToDto preservation** | Keep methods, but return typed records | The transformation logic (ObjectId→string, DateTime→ISO, BSON normalization, computed fields) is still needed. Only the anonymous-object return type changes. |
| **Frontend type generation** | `openapi-typescript` → committed `.d.ts` file | Generated types are checked into source control so the web build does not require a running API. CI validates that committed types match the spec. |
| **Contract enforcement** | Keep existing `*.contract.json` files, validate against OpenAPI spec | Gradual migration. No breaking change to the existing contract-diff tool. The contract files serve as a human-readable subset of the API contract. |
| **PropertyNameCaseInsensitive** | `true` (already default in ASP.NET Core, made explicit) | Required alongside `SnakeCaseLower` so that incoming camelCase or mixed-case JSON also works. Defense-in-depth. |

### 5.1 Options Considered and Rejected

**Option: Per-property `[JsonPropertyName]` attributes**
- Rejected because: every DTO property with an underscore needs an attribute; easy to forget; blocks future auto-generation; verbose boilerplate.

**Option: Global `JsonNamingPolicy.CamelCase` (keep frontend using camelCase)**
- Rejected because: the existing codebase — including 30 contract files, all MapToDto methods, and all TypeScript interfaces — uses snake_case. Reversing the frontend would be a larger change.

**Option: Frontend adapts to backend PascalCase**
- Rejected because: the API responses already use snake_case via MapToDto. The frontend expects snake_case. This would be a rewrite of both layers.

**Option: Generate types at web build time (not committed)**
- Rejected because: requires a running API during every web build. Committed generated types enable CI-only validation without blocking local development.

---

## 6. Scope Boundaries

### In Scope

- ASP.NET Core API project → Frontend HTTP contract (request body deserialization + response serialization)
- CI contract enforcement (contract-diff validation against OpenAPI spec)
- OpenAPI spec generation from API code
- Frontend TypeScript types derived from OpenAPI spec
- Documentation updates (engineering standards)

### Out of Scope

| Area | Reason |
|------|--------|
| **Worker app** (`apps/worker/`) | Separate process, own `Program.cs`, no `ConfigureHttpJsonOptions`. No shared JSON config with API project. |
| **Google OAuth integration** | ASP.NET Core middleware handles OAuth. Uses its own serialization paths unaffected by `ConfigureHttpJsonOptions`. |
| **FYERS API calls (backend)** | Uses `HttpClient` with separate `JsonSerializerOptions`. Not affected by ASP.NET Core HTTP pipeline JSON config. |
| **Telegram Bot (Worker)** | Lives in Worker app (separate process). Uses `PostAsJsonAsync` with default options (has a pre-existing PascalCase serialization issue — independent of this change). |
| **MongoDB BSON serialization** | MongoDB driver uses its own BSON serialization. Not affected by `System.Text.Json` config. |
| **Redis push event serialization** | Already uses explicit `JsonNamingPolicy.SnakeCaseLower` in `PushFanOutService.cs`. Not affected by global config. |

---

## 7. Performance Analysis

### 7.1 How SnakeCaseLower Works

`JsonNamingPolicy.SnakeCaseLower` is an implementation of `JsonNamingPolicy` that converts property names to lowercase with underscores:

```csharp
// "SignalTypeId" → "signal_type_id"
// Conversion happens ONCE per type
```

**Internally:**
1. On first serialization of a type, `System.Text.Json` calls `ConvertName()` per property
2. Results are cached in internal metadata (concurrent dictionary)
3. All subsequent serializations use the cached mapping — **zero allocation, O(1) lookup**

### 7.2 Cost Comparison

| Approach | Startup Cost | Per-Request Cost | Maintenance Cost |
|----------|-------------|------------------|------------------|
| `[JsonPropertyName]` | None | None (compile-time attribute read) | High (must add for every property) |
| `SnakeCaseLower` policy | ~1ms per type (one-time) | Dictionary lookup (~50ns) | Zero (automatic) |
| Anonymous `new { }` with explicit names | None | None (hardcoded at compile time) | Medium (MapToDto maintenance) |

### 7.3 Conclusion

Performance impact is **negligible**. The one-time startup cost of ~1ms per DTO type is insignificant compared to application startup (~seconds). Per-request cost of a dictionary lookup (~50ns) is far below measurable impact.

---

## 8. External Integration Safety Analysis

### 8.1 How ConfigureHttpJsonOptions Works

```csharp
builder.Services.ConfigureHttpJsonOptions(options => { ... });
```

This method configures options **only** for ASP.NET Core's own HTTP JSON pipeline:
- Binding request bodies from endpoint parameters
- Serializing response bodies via `Results.Ok()`, `Results.Json()`, etc.

It does **NOT** affect:

| Mechanism | Used By | Safe? |
|-----------|---------|-------|
| `HttpClient.PostAsJsonAsync()` | FYERS API calls, Telegram bot | ✅ Separate options |
| `HttpClient.ReadFromJsonAsync()` | FYERS response parsing | ✅ Separate options |
| `JsonSerializer.Serialize()` standalone | FYERS auth service | ✅ Default options (not web defaults) |
| `JsonSerializer.Deserialize()` standalone | Various utility code | ✅ Default options |
| `BsonSerializer` | MongoDB driver | ✅ Completely different serializer |
| ASP.NET Core OAuth middleware | Google/Microsoft/Facebook auth | ✅ Middleware internals, not our JSON |
| `WriteAsJsonAsync()` on `HttpResponse` | CSRF middleware, Push endpoints | ✅ **Will be affected** — which is correct (these are HTTP responses) |

### 8.2 Explicit SnakeCaseLower Already in Use

Some parts of the codebase already use `SnakeCaseLower` explicitly:

| Location | Purpose | Already has it? |
|----------|---------|-----------------|
| `PushFanOutService.cs` | Redis push event serialization | ✅ Yes (explicit) |
| `RedisPushEventPublisher.cs` (Worker) | Worker-side Redis push | ✅ Yes (explicit) |
| `FyersMarketDataProvider.cs` (Worker) | FYERS response deserialization | ✅ Yes (explicit) |

This proves the naming policy is already used successfully — we are just making it the default.

### 8.3 Worker App Independence

The Worker app (`apps/worker/`) is a separate .NET process with its own `Program.cs`. It has:
- No `ConfigureHttpJsonOptions` call (it does not serve HTTP endpoints)
- No shared JSON serializer config with the API project
- Its own `JsonSerializerOptions` where needed (explicit and local)

**No impact from API project changes.**

---

## 9. Phased Implementation Plan

### Phase 0 — Audit & document (estimated effort: small)

**Goal**: Document the decision before any code changes.

**Actions:**
1. Add a new "API Contract Standards" section to `docs/engineering-standards.md`
2. Specify: all HTTP JSON property names use **snake_case**
3. Specify: enforced via `JsonNamingPolicy.SnakeCaseLower` globally
4. Specify: frontend types generated from OpenAPI spec (future)
5. Record scope boundaries (Worker app, external integrations out of scope)

**Verification**: Document reviewed and committed.

---

### Phase 1 — Backend: OpenAPI generation scaffolding (estimated effort: small)

**Goal**: Establish the OpenAPI endpoint pattern with no functional change.

**Actions:**
1. Add `builder.AddOpenApi()` after `builder.AddSignalStackBootstrapConfiguration(...)` in `apps/api/Program.cs`
2. Add `app.MapOpenApi()` after endpoint mappings
3. The spec will be generic initially (anonymous types show as `{}`)

```csharp
// After bootstrap configuration and service registration
builder.AddOpenApi();

// ...

// After app.Map* endpoint registrations
app.MapOpenApi();
```

**Note**: `Microsoft.AspNetCore.OpenApi` is built into .NET 10 — no NuGet package needed.

**Why first**: Establishes the `/openapi/v1.json` endpoint that subsequent phases depend on.

**Verification**: `curl http://localhost:5000/openapi/v1.json` returns valid OpenAPI 3.0 JSON.

---

### Phase 2 — Backend: Global naming policy (estimated effort: small)

**Goal**: Fix all request binding bugs with one config change.

**Actions:**
1. Add `ConfigureHttpJsonOptions` block in `apps/api/Program.cs`:

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
```

2. No changes to request DTOs needed — `signal_type_id` now correctly binds to `SignalTypeId` for all endpoints.
3. No changes to `MapToDto` methods needed — anonymous objects use explicit property names unaffected by naming policy.

**What this fixes immediately:**
- `POST /signals/subscriptions/` — `signal_type_id`, `rme_configuration`
- `POST /signals/subscriptions/{id}/versions` — `rme_configuration`
- All other endpoints with compound snake_case request fields

**Verification**: Send the failing payload → receives HTTP 201. All existing GET endpoints return same responses.

---

### Phase 3 — Backend: Migrate response DTOs to typed records (estimated effort: medium)

**Goal**: Enable accurate OpenAPI schemas for all endpoints.

**Background**: Currently, `MapToDto` methods return anonymous `new { ... }` objects. These produce generic `{}` schemas in OpenAPI — useless for documentation and codegen. Typed records produce meaningful schemas.

**Pattern for migration:**

```csharp
// Before: anonymous object
private static object MapToDto(SignalSubscriptionDocument sub) => new
{
    id = sub.Id.ToString(),
    signal_type_id = sub.SignalTypeId,
    is_paused = sub.Status == SubscriptionStatus.Paused,
    // ...
};

// After: typed response record
public sealed record SubscriptionResponse(
    string Id,
    string UserId,
    string Name,
    string SignalTypeId,
    // ...
    bool IsPaused
);

private static SubscriptionResponse MapToDto(SignalSubscriptionDocument sub) => new(
    Id: sub.Id.ToString(),
    UserId: sub.UserId,
    SignalTypeId: sub.SignalTypeId,
    IsPaused: sub.Status == SubscriptionStatus.Paused,
    // ...
);
```

**Why MapToDto still exists**: It handles non-trivial transformations that cannot be automated:
- `ObjectId.ToString()` — type conversion
- `DateTime.ToString("o")` — format conversion
- `BsonNormalizer.NormalizeBsonDocument()` — BSON to plain object
- Computed fields — `is_paused`, `has_pending_version`
- Nested object construction — `current_version` block

**Migration order** (by impact):

| Order | Endpoint Group | Effort | Reason |
|-------|---------------|--------|--------|
| 1 | Signal subscriptions | Small | The trigger bug, narrow scope |
| 2 | Notifications | Medium | ~14 properties, heavy usage |
| 3 | Auth endpoints | Medium | ~15 endpoints, simple per-endpoint |
| 4 | Portfolio / reconciliation | Medium | Shared patterns |
| 5 | Admin endpoints | Medium | ~20+ endpoints |
| 6 | Backtesting / optimisation | Small | Already typed records, just verify |

**Verification**: Existing E2E tests pass (response JSON is identical — same snake_case property names). OpenAPI spec now shows precise schemas.

---

### Phase 4 — Frontend: Auto-generate types from OpenAPI (estimated effort: medium)

**Goal**: Eliminate hand-written TypeScript DTOs — derive from backend code.

**Rationale**: Currently, every API change requires manually updating TypeScript interfaces. This is error-prone and breaks silently (TypeScript cannot validate JSON property names at runtime).

**Actions:**
1. Add `openapi-typescript` as dev dependency in `apps/web/package.json`:

```bash
npm --workspace @signalstack/web add -D openapi-typescript
```

2. Create a type generation script in `apps/web/scripts/typegen-api.mjs`:

```javascript
// Fetches /openapi/v1.json and generates TypeScript types
import { execSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';

const API_BASE = process.env.API_BASE_URL || 'http://localhost:5000';
const OUT_PATH = 'src/lib/api-types.d.ts';

const res = await fetch(`${API_BASE}/openapi/v1.json`);
const spec = await res.json();
writeFileSync('openapi.json', JSON.stringify(spec, null, 2));

execSync(`npx openapi-typescript openapi.json -o ${OUT_PATH}`, { stdio: 'inherit' });
console.log(`Types generated at ${OUT_PATH}`);
```

3. Add npm script in `apps/web/package.json`:

```json
"scripts": {
  "typegen:api": "node scripts/typegen-api.mjs"
}
```

4. Replace hand-written TypeScript interfaces with generated types:

```typescript
// Before
interface SubscriptionDto {
  id: string;
  signal_type_id: string;
  is_paused: boolean;
  // ...
}

// After
import type { components } from '@/lib/api-types';
type SubscriptionDto = components['schemas']['SubscriptionResponse'];
```

5. Add CI validation: run `npm run typegen:api` and `git diff --exit-code` to fail if generated types do not match committed ones.

**Verification**: `npm run typegen:api` runs clean. `tsc --noEmit` passes with zero type errors.

---

### Phase 5 — contract-diff: Validate against OpenAPI spec (estimated effort: medium)

**Goal**: The contract files actually enforce the API contract.

**Rationale**: Currently, contract-diff validates the JSON shape of contract files but never checks whether the API implementation matches. Phase 5 bridges this gap.

**Actions:**
1. Update `packages/testing/src/contract-diff.mjs`:

Add a `--openapi <path>` flag that loads an OpenAPI 3.0 spec. For each `*.contract.json`:
- Verify `endpoint` + `method` exists in the OpenAPI paths
- For each `requestFields.path`, verify the field exists in the spec's `requestBody` schema
- For each `responseFields.path`, verify the field exists in the spec's response schema (200 status code)
- Fail CI (exit code 1) on any mismatch

2. Add to `.github/workflows/ci.yml` a step that:
   - Builds the API
   - Starts it briefly to fetch `/openapi/v1.json`
   - Runs `npm run contract-diff --openapi openapi.json`

**Verification**: `npm run contract-diff --openapi openapi.json` passes for all 30 contracts. Intentionally adding a wrong field to a contract fails CI.

---

### Phase 6 — CI: End-to-end enforcement (estimated effort: small)

**Goal**: Fully automated pipeline catches contract mismatches before merge.

**Actions** — Add to `.github/workflows/ci.yml`:

1. **OpenAPI spec generation**: After dotnet build, start API → fetch `/openapi/v1.json` → save as build artifact
2. **Contract validation**: Run `contract-diff --openapi openapi.json` against all 30 contracts
3. **Frontend type validation**: Run `typegen:api` → `git diff --exit-code` to ensure committed types match spec
4. All three steps must pass for PR merge

**Verification**: Whole CI pipeline green with all new steps. Deliberate contract mismatch causes CI failure.

---

## 10. Verification Matrix

| Phase | Check | Method | Expected |
|-------|-------|--------|----------|
| 0 | Docs updated | Review | `engineering-standards.md` has API Contract Standards section |
| 1 | OpenAPI endpoint | `curl /openapi/v1.json` | Valid OpenAPI 3.0 document returned |
| 2 | Bug fixed | POST failing payload | HTTP 201 instead of 400 |
| 2 | No regression | GET existing endpoints | Same responses as before |
| 3 | Response same | E2E tests | All pass (same JSON) |
| 3 | Accurate schemas | OpenAPI spec | Response fields documented, not `{}` |
| 4 | Frontend types | `npm run typegen:api` | Clean run, `tsc --noEmit` passes |
| 4 | CI validation | Add typegen + diff-check step | Fail if types drift from spec |
| 5 | Contract validation | `contract-diff --openapi` | 30/30 contracts match |
| 6 | CI pipeline | Full pipeline run | All steps green |

---

## 11. Relevant Files

| File | Phases | Change |
|------|--------|--------|
| `apps/api/Program.cs` | 1, 2 | Add `AddOpenApi()`, `MapOpenApi()`, `ConfigureHttpJsonOptions` |
| `apps/api/Signals/SignalSubscriptionEndpoints.cs` | 2, 3 | Add typed `SubscriptionResponse` record; migrate MapToDto |
| `apps/api/Audit/AdminAuditEndpoints.cs` | 3 | Migrate MapToEntry to typed record |
| `apps/api/Notifications/NotificationEndpoints.cs` | 3 | Migrate MapToFeedDto to typed record |
| `apps/api/Auth/AuthEndpoints.cs` | 3 | Migrate anonymous responses to typed records |
| `apps/api/Portfolio/ReconciliationEndpoints.cs` | 3 | Migrate anonymous responses to typed records |
| `apps/api/Portfolio/PortfolioEndpoints.cs` | 3 | Migrate holdings endpoint to typed records |
| `apps/web/package.json` | 4 | Add `openapi-typescript`, `typegen:api` script |
| `apps/web/scripts/typegen-api.mjs` | 4 | Created — fetches spec + generates types |
| `apps/web/src/lib/api-types.d.ts` | 4 | Generated TypeScript types (committed) |
| `apps/web/src/app/(user)/signals/page.tsx` | 4 | Replace `SubscriptionDto` with generated types |
| `apps/web/src/app/(user)/page.tsx` | 4 | Replace `HoldingDto`, `PortfolioSummaryDto` with generated types |
| `apps/web/src/app/(user)/notifications/page.tsx` | 4 | Replace `NotificationDto` with generated types |
| `packages/testing/src/contract-diff.mjs` | 5 | Add `--openapi` validation mode |
| `.github/workflows/ci.yml` | 5, 6 | Add OpenAPI fetch + validation + typegen steps |
| `docs/engineering-standards.md` | 0 | Add API Contract Standards section |

---

## Appendix A: Endpoint Impact Matrix

| Endpoint | Current State | After Phase 2 | After Phase 3 |
|----------|--------------|---------------|---------------|
| `GET /signals/subscriptions` | Works (snake_case response via MapToDto) | ✅ Works (unchanged) | ✅ Works (typed record → snake_case via policy) |
| `POST /signals/subscriptions` | ❌ Broken (`signal_type_id` not binding) | ✅ Fixed (SnakeCaseLower converts to SignalTypeId) | ✅ Same |
| `PUT /signals/subscriptions/{id}` | ✅ Works (only single-word fields) | ✅ Still works | ✅ Same |
| `POST /signals/subscriptions/{id}/versions` | ❌ Broken (`rme_configuration` not binding) | ✅ Fixed | ✅ Same |
| `GET /portfolio/holdings` | ✅ Works (snake_case via anonymous) | ✅ Works | ✅ Works (typed record) |
| `GET /portfolio/impact` | ✅ Works (snake_case via anonymous) | ✅ Works | ✅ Works (typed record) |
| `POST /admin/impersonation/start` | ⚠️ Maybe broken (`target_user_id` via ReadFromJsonAsync) | ⚠️ Still same (ReadFromJsonAsync uses own defaults) | ✅ If migrated from ReadFromJsonAsync to endpoint binding |
| `POST /execution/intent/signed-payload` | ⚠️ Maybe broken (`order_type` vs `orderType`) | ✅ Fixed (SnakeCaseLower converts to OrderType) | ✅ Same |

## Appendix B: ReadFromJsonAsync Endpoints (need separate review)

These 17 endpoints use `ReadFromJsonAsync<T>()` with explicit cancellation token but **no explicit JsonSerializerOptions**. They use `JsonSerializerDefaults.Web` independently of the global config:

| Endpoint | DTO |
|----------|-----|
| `POST /admin/impersonation/start` | `StartImpersonationRequest` |
| `POST /admin/phase/transition` | `PhaseTransitionRequest` |
| `POST /admin/phase/sebi-opinion` | `SebiOpinionRequest` |
| `POST /admin/phase/gate` | `GateUpdateRequest` |
| `POST /admin/pentest` | `SchedulePentestRequest` |
| `PATCH /admin/pentest/{id}/findings` | `AddFindingRequest` |
| `PATCH /admin/pentest/{id}/findings/{findingId}` | `UpdateFindingRequest` |
| `PATCH /admin/pentest/{id}/status` | `UpdatePentestStatusRequest` |
| `PUT /admin/config/{key}` | `UpdateConfigRequest` |
| `POST /admin/config/{key}/reset` | `ResetConfigRequest` |
| `POST /admin/chaos-exercises` | `RecordExerciseRequest` |
| `PATCH /admin/chaos-exercises/{id}` | `UpdateExerciseRequest` |
| `POST /telegram/link-chat` | `LinkChatRequest` |
| `POST /telegram/replace-token` | `ReplaceTokenRequest` |
| `POST /auth/linked-identities/unlink` | `UnlinkRequest` |
| `POST /auth/admin-recover` | `AdminRecoverRequest` |
| `POST /auth/accept-legal` | `AcceptLegalRequest` |

**Recommendation**: These should be migrated to endpoint parameter binding (same as other endpoints) so they benefit from the global naming policy. This is a Phase 3 refinement task.

---

*End of plan.*
