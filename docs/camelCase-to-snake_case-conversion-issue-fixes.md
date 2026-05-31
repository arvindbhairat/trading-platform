# API Contract Standardization — Phase 3: Typed Response Records

> **Context**: Phase 1 (OpenAPI scaffolding), Phase 2 (global `SnakeCaseLower`), Phase 4 (frontend typegen), Phase 5 (contract-diff), and Phase 6 (CI enforcement) are all already in place. This document covers **Phase 3 only** — the sole remaining work item.

---

## 1. Problem Statement

The API project uses `JsonNamingPolicy.SnakeCaseLower` (Phase 2, `Program.cs` line 66) to serialize all HTTP JSON responses with snake_case property names. This is the correct, intentional behaviour.

However, most endpoint handlers return anonymous types:

```csharp
return Results.Ok(new
{
    id = doc.Id.ToString(),
    signal_type_id = doc.SignalTypeId,   // manually snake_case
    is_paused = doc.Status == SubscriptionStatus.Paused,
    // ...
});
```

**Why this is a problem:**

1. **OpenAPI spec produces `{}` schemas** — Anonymous types have no compile-time identity, so the OpenAPI generator cannot describe their shape. The spec at `/openapi/v1.json` shows `{}` for the response schema of any endpoint returning an anonymous type.

2. **Frontend auto-generated types are empty** — `openapi-typescript` (Phase 4) reads the OpenAPI spec and generates TypeScript type definitions. With `{}` schemas, the generated `api-types.d.ts` has empty `schemas: {}`.

3. **contract-diff cannot validate response shapes** — Phase 5's `--openapi` flag cross-references contracts against the OpenAPI spec, but with `{}` schemas there's nothing to validate against.

4. **No single source of truth** — Response shapes exist only as implicit runtime objects. Any developer introducing a new endpoint must manually ensure property names match snake_case. TypeScript types are hand-written in the frontend and drift silently from the API.

---

## 2. Solution: Typed `sealed record` Response DTOs

### 2.1 The Pattern

Replace every anonymous `new { }` response with a named `sealed record`:

```csharp
// ── Before: anonymous type ─────────────────────────────────────
return Results.Ok(new
{
    id = sub.Id.ToString(),
    signal_type_id = sub.SignalTypeId,
    is_paused = sub.Status == SubscriptionStatus.Paused,
    has_pending_version = sub.PendingVersion is not null,
    version_count = sub.Versions?.Count ?? 0,
    created_at = sub.CreatedAt.ToString("o"),
});

// ── After: typed record ────────────────────────────────────────
public sealed record SubscriptionResponse(
    string Id,
    string SignalTypeId,
    bool IsPaused,
    bool HasPendingVersion,
    int VersionCount,
    string CreatedAt
);

return Results.Ok(new SubscriptionResponse(
    Id: sub.Id.ToString(),
    SignalTypeId: sub.SignalTypeId,
    IsPaused: sub.Status == SubscriptionStatus.Paused,
    HasPendingVersion: sub.PendingVersion is not null,
    VersionCount: sub.Versions?.Count ?? 0,
    CreatedAt: sub.CreatedAt.ToString("o")
));
```

### 2.2 Why This Works

- `JsonNamingPolicy.SnakeCaseLower` automatically converts `SubscriptionResponse.IsPaused` → `"is_paused"` in the JSON output — no manual property naming required.
- The OpenAPI generator sees `SubscriptionResponse` as a named type and produces a full schema with all properties, their types, and nullability.
- `openapi-typescript` generates an exact TypeScript type from that schema.
- `contract-diff` can validate that response contracts match the schema.
- C# compiler catches missing/renamed properties at build time.

### 2.3 What To Keep

Existing **`MapToDto` methods** that perform non-trivial transformations (ObjectId→string, DateTime→ISO 8601, BSON normalization, computed fields) should be **kept** — they just return the typed record instead of `object`:

```csharp
// Keep the method, change the return type
private static SubscriptionResponse MapToDto(SignalSubscriptionDocument sub) => new(
    Id: sub.Id.ToString(),
    SignalTypeId: sub.SignalTypeId,
    IsPaused: sub.Status == SubscriptionStatus.Paused,
    // ... same transformation logic
);
```

---

## 3. Scope: Endpoints Currently Using Anonymous Types

Survey conducted 2026-05-31. ~80-90 anonymous `new { }` instances across ~21 files.

### 3.1 Auth (`AuthEndpoints.cs`) — ~15 instances

| Endpoint | Properties | Notes |
|---|---|---|
| `GET /auth/csrf` | 1 | Simple |
| `GET /auth/me` | 8 | `sub`, `email`, `name`, `picture`, `locale`, `role`, `approved`, `provider` |
| `GET /auth/session/status` | 3-4 per branch | **9 branches** with different shapes. Consider a single `SessionStatusResponse` with nullable fields instead |
| `POST /auth/logout` | 1 | Simple |
| `POST /auth/step-up/init` | 1 | Simple |
| `GET /auth/step-up/status` | 3 | Simple |
| `GET /auth/link-provider` | 1 | Simple |
| `POST /auth/accept-legal` | 1 | Simple |
| Test helpers | 1 each | Low priority |

**Priority: High** — `session/status` is the most complex and most impactful.

### 3.2 Notifications (`NotificationEndpoints.cs`) — 3 instances

| Endpoint | Properties | Notes |
|---|---|---|
| `GET /notifications/unread-count` | 2 | `total_unread`, `critical_unread` |
| `POST /notifications/{id}/read` | 1 | Simple |
| `POST /notifications/mark-all-read` | 2 | Simple |

**Note**: List and types endpoints already use named records (`NotificationFeedResponse`, `NotificationTypeResponse`). Only these 3 small ones need conversion.

### 3.3 Signal Subscriptions (`SignalSubscriptionEndpoints.cs`) — 8 instances

| Endpoint | Properties | Notes |
|---|---|---|
| POST pause/resume responses | 1-2 | Simple status responses |
| POST version create | 5 | Multiple computed fields |
| POST version discard | 2 | Simple |
| DELETE subscription | 1 | Simple |

**Note**: GET endpoints already use `SubscriptionResponse` named record via `MapToDto`.

### 3.4 Admin — Config (`AdminConfigEndpoints.cs`) — 5 instances

| Endpoint | Notes |
|---|---|
| `GET /admin/config` | Wraps `result` list |
| `GET /admin/config/categories` | Simple list |
| `GET /admin/config/{key}` | Wraps `MapToEntry` |
| `PUT /admin/config/{key}` | Wraps `MapToEntry` |
| `POST /admin/config/{key}/reset` | Wraps `MapToEntry` |

### 3.5 Admin — Users (`AdminUserEndpoints.cs`) — 8+ instances

| Endpoint | Properties | Notes |
|---|---|---|
| `GET /admin/users` | 1 | Wraps user list |
| `GET /admin/users/pending/count` | 1 | Simple count |
| `POST .../approve` | 1 | Simple |
| Deactivate/reactivate | 5, 2 | Status + user info |
| Signal-suspend/enable | 3, 2 | Combined responses |

### 3.6 Admin — Universe Sync (`UniverseSyncEndpoints.cs`) — 9+ instances

**Heaviest usage of anonymous types in the codebase.**

| Endpoint | Notes |
|---|---|
| `POST /admin/universe/upload/preview` | Deeply nested anonymous with ~20 total properties. **Most complex conversion.** |
| `POST /admin/universe/upload/commit` | 5 props |
| `GET /admin/universe/uploads` | Wraps list of anonymous |
| `POST /admin/universe/upload/rollback` | 2 props |
| `GET /admin/universe/sync-health` | 4 props, map returns anonymous |
| `POST /admin/universe/reseed` | 4 props |
| `GET /admin/universe/hds-status` | 3 props, map returns anonymous |
| `GET /admin/universe/work-queue` | 2 props, map returns anonymous |
| `POST /admin/universe/probe` | 2 branches |

### 3.7 Admin — System Health (`AdminSystemHealthEndpoints.cs`) — 12+ instances

6 helper methods all return `object` with anonymous types internally:
- `GetDailyBreakdownAsync`
- `BuildAdminFyersTokenAsync`
- `BuildMarketDataProviderAsync`
- `BuildTelegramPipelineAsync`
- `BuildKillSwitchAsync`
- `BuildMarketHaltAsync`

The main `GET /admin/system-health` endpoint wraps all 6 component sections in a single large anonymous type.

### 3.8 Admin — Incidents (`AdminIncidentEndpoints.cs`) — 2+ instances

`MapIncident` method (line 370) is a **dedicated mapper method** returning anonymous type with 10 properties. This is halfway to a proper DTO — the method exists, just change return type from `object` to a named record.

### 3.9 Admin — Legal Posture (`AdminLegalPostureEndpoints.cs`) — 1 instance

Large inline anonymous with nested anonymous sub-objects (`sebi_opinion`, `runbook_catalog`).

### 3.10 Admin — Other

| File | Instances |
|---|---|
| `AdminKillSwitchEndpoints.cs` | 2 |
| `AdminChaosExerciseEndpoints.cs` | 2 |
| `AdminPenetrationTestEndpoints.cs` | 2 |
| `AdminSignalTypeEndpoints.cs` | 1 |
| `AdminPhaseEndpoints.cs` | 3 |
| `AdminPositionEndpoints.cs` | Several inline |
| `ImpersonationEndpoints.cs` | 1 |
| `TradingCalendarEndpoints.cs` | 4 (1 prop each) |

### 3.11 RME Advisory (`RmeAdvisoryEndpoints.cs`) — 3 instances

| Endpoint | Properties | Notes |
|---|---|---|
| `GET /rme/advisory` | ~16 | Inline, dense |
| `GET /rme/health` | ~12 | Inline, dense |
| `GET /rme/channel-status` | ~5 | Inline |

### 3.12 Execution (`ExecutionEndpoints.cs`) — 6+ instances

| Endpoint | Properties | Notes |
|---|---|---|
| `GET /execution/pre-flight` | 4 | Nested `Select` produces anonymous |
| `GET /execution/order-context` | ~17 | **Second most complex conversion** |
| `POST /execution/intent/signed-payload` | 4 | |
| `POST /execution/intent/callback` | 4 | |
| `GET /execution/intent/pending-confirmations` | 1 | Nested list |

### 3.13 FYERS (`FyersEndpoints.cs`) — 6 instances

| Endpoint | Properties | Notes |
|---|---|---|
| `POST /fyers/auth/init` | 1 | `auth_url` |
| `GET /fyers/status` | 7 | Token status info |
| `POST /fyers/reauth` | 1 | `auth_url` |
| `GET /fyers/token` | 2 | Token data |
| `GET /fyers/quotes` | 3 | Quote responses |

Note: `GET /fyers/quotes` branches by response type (success/error/cached). Consider a discriminated union or nullable fields.

### 3.14 Other Files (scattered)

| File | Instances | Notes |
|---|---|---|
| `UserProfileExtensions.cs` | 3 | 9 props, 3 props, 1 prop |
| `PrivacyRequestEndpoints.cs` | 4+ | Deeply nested in `BuildDataExport` |
| `TelegramBotEndpoints.cs` | 3 | Simple |
| `TelegramLinkingEndpoints.cs` | 3 | Simple |
| `DataBreachEndpoints.cs` | 2 | Simple |
| `SymbolMasterEndpoints.cs` | 2 | Simple |
| `AdminAuditEndpoints.cs` | 2 | Simple |
| `Program.cs` | 2 | Rate limiter, auth probe |
| `ChartEndpoints.cs` | 0 | **Already clean — uses `LastCandleResponse`** |
| `Portfolio/` (all files) | 0 | **Already clean** |
| `Backtesting/` (all files) | 0 | **Already clean** |

---

## 4. Conversion Rules

### 4.1 Naming Convention

Record property names use **PascalCase** in C#. The `SnakeCaseLower` policy converts them to snake_case in JSON output automatically.

| C# record property | JSON output |
|---|---|
| `string Id` | `"id"` |
| `bool IsPaused` | `"is_paused"` |
| `int VersionCount` | `"version_count"` |
| `string? RequesterEmail` | `"requester_email"` |

### 4.2 File Organization

- Place the record definition **in the same file** as the endpoint class that uses it, at file scope (outside the class).
- Prefix the record name with the endpoint group name for clarity: `AuthSessionStatusResponse`, `FyersStatusResponse`, `UniverseUploadPreviewResponse`.
- If a record is shared across multiple endpoint files within the same directory, extract it to a shared file or a `Responses.cs` file in the same folder.

### 4.3 Nullability

- **Required fields**: Non-nullable (`string Id`)
- **Optional fields**: Nullable (`string? Picture`)
- Use nullable annotations consistently with what the anonymous type intended. If the anonymous type could omit a key, the record property should be nullable.

### 4.4 Type Mappings

| Anonymous type value | Record property type |
|---|---|
| `sub.Id.ToString()` | `string Id` |
| `doc.CreatedAt.ToString("o")` | `string CreatedAt` |
| `sub.Status == ...` | `bool IsPaused` |
| `sub.PendingVersion is not null` | `bool HasPendingVersion` |
| `sub.Versions?.Count ?? 0` | `int VersionCount` |
| `doc.FyersAppType` (enum) | `string FyersAppType` (or keep as enum if the serializer handles it) |
| `BsonNormalizer.Normalize(...)` | `object? Params` (or typed if shape is known) |

### 4.5 Multi-Branch Endpoints

Some endpoints return different shapes based on conditions (e.g., `session/status` has 9 branches). Options:

**Option A: Single record with nullable fields** (preferred when branches are similar):
```csharp
public sealed record SessionStatusResponse(
    string State,
    string Role,
    string ExpiresAt,
    SessionStepUp? StepUp  // null when not applicable
);
```

**Option B: Polymorphic records** (use when shapes differ fundamentally):
```csharp
public abstract record AuthResponse;
public sealed record AuthSuccess(string Token) : AuthResponse;
public sealed record AuthError(string Error) : AuthResponse;
```

Start with Option A. Only use Option B when the response shapes genuinely cannot share fields.

### 4.6 Nested Objects

For properties that are themselves objects, create nested records:

```csharp
public sealed record SebiOpinion(
    bool Received,
    string? ReceivedDate
);

public sealed record LegalPostureResponse(
    string CurrentPhase,
    int ApprovedUserCount,
    SebiOpinion SebiOpinion,
    // ...
);
```

---

## 5. Verification

After each file is converted:

1. **TypeScript compilation**: `npx tsc --noEmit` in `apps/web` must pass
2. **OpenAPI spec**: `curl /openapi/v1.json` should show concrete schemas instead of `{}` for converted endpoints
3. **No functional change**: The JSON output must be identical — same property names, same types. The only change is the mechanism: naming policy handles the conversion instead of manual snake_case in anonymous types.
4. **CI pipeline**: After sufficient conversions, run the CI pipeline — the typegen diff-check and contract-diff steps should start validating real data.

---

## 6. Recommended Order

Convert areas that intersect with the already-fixed frontend admin pages first (faster verification):

1. **Admin endpoints** — These frontend mismatches were just fixed (19 properties in 4 admin pages). Converting the API responses ensures the OpenAPI schemas for admin endpoints are correct.
2. **Auth** — `session/status` is the most impactful single endpoint (drives the entire frontend routing).
3. **FYERS** — Token status and quotes are heavily used.
4. **Notifications** — Small, quick wins.
5. **Execution** — The 17-property order context response is a high-value target.
6. **RME Advisory** — Dense but stable responses.
7. **Signal Subscriptions** — Already has `SubscriptionResponse` for GET; just the mutation responses need conversion.
8. **Universe Sync** — Most complex; save for last.
9. **All remaining scattered files**.

---

## 7. Files That Need No Changes

These areas already use named response records and are **correct as-is**:

- `apps/api/Portfolio/` — All endpoints (PortfolioEndpoints.cs, ReconciliationEndpoints.cs)
- `apps/api/Backtesting/` — All endpoints
- `apps/api/Chart/ChartEndpoints.cs` — Uses `LastCandleResponse`

---

## 8. The Bigger Picture

Once Phase 3 is complete:

- `openapi-typescript` generates a complete `api-types.d.ts` from the live OpenAPI spec
- The committed `src/lib/api-types.d.ts` is validated in CI (`git diff --exit-code`)
- New frontend code can import types directly: `import type { components } from '@/lib/api-types'`
- `contract-diff --openapi openapi.json` validates all 30 contracts against real schemas
- Any future API change that modifies a response shape automatically updates the generated types, and CI catches the drift

**End state**: Backend C# records are the single source of truth for the API contract. Frontend types are derived automatically. CI enforces consistency.
