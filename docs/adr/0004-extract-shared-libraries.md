# 0004: Extract Shared Libraries From API Project

**Status:** Accepted  
**Date:** 2026-05-25  
**Deciders:** Architecture Team  
**References:** Worker crash (logs.1779699920582.log), API crash (logs.1779699261124.log)

---

## Problem Statement

The background Worker service references the API project directly:

```xml
<!-- apps/worker/SignalStack.Worker.csproj, line 26 -->
<ProjectReference Include="..\api\SignalStack.Api.csproj" />
```

This causes three systemic problems:

### 1. Runtime framework mismatch (immediate production crash)

The API project uses `Microsoft.NET.Sdk.Web`, which targets `Microsoft.AspNetCore.App`. By transitively pulling in ASP.NET Core assemblies, the Worker also requires `Microsoft.AspNetCore.App` at runtime — but its Dockerfile uses `dotnet/runtime:10.0`, which only includes `Microsoft.NETCore.App`.

```
App: /app/SignalStack.Worker.dll
Framework: 'Microsoft.AspNetCore.App', version '10.0.0' (x64)
No frameworks were found.
```

The Worker must use the heavier `dotnet/aspnet:10.0` image despite having no web concerns of its own.

### 2. Tight coupling between deployment units

Any change to the API project — adding a NuGet package, changing a route handler signature, modifying a middleware class — forces a rebuild and redeploy of the Worker. The Worker should only need to rebuild when its own logic or shared domain contracts change.

### 3. Blurred architectural boundary

The API project has become a God project containing 16+ internal namespaces (`Admin`, `Backtesting`, `Historical`, `Notifications`, `Portfolio`, `Signals`, `Universe`, etc.) that mix:

- **Domain logic and data access** (documents, repositories, services) — needed by the Worker  
- **API presentation** (endpoints, middleware, auth) — irrelevant to the Worker  

There is no compile-time enforcement of this boundary, so the pattern will repeat as the codebase grows.

---

## Current Dependency Graph

```
packages/config ────────────────────────────────────────────────┐
    ↑          ↑           ↑           ↑           ↑           ↑
    │          │           │           │           │           │
packages/   packages/   packages/   packages/   packages/   packages/
market-data migrations  domain      storage     signals     notifications
    ↑          ↑           ↑           ↑           ↑           ↑
    │          │           │           │           │           │
    └──────────┼───────────┼───────────┴───────────┴───────────┘
               │           │
          apps/api     apps/worker
               │           
          packages/historical

**Key improvement:** apps/worker no longer references apps/api. All shared dependencies flow through packages.
```

---

## Full Audit: All Project References

| Project | References | Issue? |
|---|---|---|
| `apps/api` | `packages/configuration`, `packages/migrations`, `packages/domain`, `packages/storage`, `packages/signals`, `packages/notifications`, `packages/historical` | Clean |
| `apps/worker` | `packages/configuration`, `packages/market-data`, `packages/migrations`, `packages/sql-migrations`, `packages/domain`, `packages/storage`, `packages/signals`, `packages/notifications`, `packages/historical` | Clean (no API ref) |
| `apps/seed` | *(none)* | Clean |
| `packages/market-data` | `packages/configuration` | Clean |
| `packages/domain` | *(none)* | Clean |
| `packages/storage` | `packages/domain`, `packages/configuration` | Clean |
| `packages/historical` | `packages/domain`, `packages/storage` | Clean |
| `packages/signals` | `packages/domain`, `packages/storage`, `packages/historical` | Clean |
| `packages/notifications` | `packages/domain`, `packages/storage` | Clean |
| `tests/api` | `apps/api`, `packages/configuration` | Expected |
| `tests/worker` | `apps/worker`, `packages/configuration` | Expected |
| `tests/migrations` | `packages/migrations` | Clean |

**All violations resolved:** `apps/worker` no longer references `apps/api`. No other application project references another application project.

---

## Worker's Package Dependencies (After Refactor)

The Worker now imports the following package namespaces instead of `SignalStack.Api.*`:

| Package | Namespaces | Nature |
|---|---|---|
| `SignalStack.Domain` | `SignalStack.Domain.Audit`, `SignalStack.Domain.Users`, `SignalStack.Domain.Backtesting`, `SignalStack.Domain.Signals`, `SignalStack.Domain.Universe`, etc. | Domain models + interfaces |
| `SignalStack.Storage` | `SignalStack.Storage.Historical`, `SignalStack.Storage.LedgerWriters`, `SignalStack.Storage.Notifications`, `SignalStack.Storage.Signals`, `SignalStack.Storage.SysConfig`, `SignalStack.Storage.Universe`, `SignalStack.Storage.Admin`, `SignalStack.Storage.Backtesting`, `SignalStack.Storage.Portfolio` | Storage implementations |
| `SignalStack.Signals` | `SignalStack.Signals.Backtesting` | Signal evaluation + backtest engine |
| `SignalStack.Notifications` | `SignalStack.Notifications`, `SignalStack.Notifications.TelegramBot` | Notification services |
| `SignalStack.Historical` | `SignalStack.Historical` | Historical OHLCV services |
| `SignalStack.MarketData` | `SignalStack.MarketData` | MDP interface + DTOs |
| `SignalStack.Configuration` | `SignalStack.Configuration.Bootstrap`, `SignalStack.Configuration.Ledger`, etc. | Config loaders |

**All Worker dependencies are now clean package references.** No reference to `apps/api` remains.

---

## Proposed Solution: Extract Domain Libraries

### Target Dependency Graph

```
packages/configuration ────────────────────────────────────────
    ↑          ↑           ↑           ↑           ↑           ↑
    │          │           │           │           │           │
packages/   packages/   packages/   packages/   packages/   packages/
market-data migrations  domain      storage     signals     notifications
                            ↑                       ↑
                            │                       │
    ┌───────────────────────┴───────────────────────┘
    │                       │
apps/api (thin)         apps/worker (clean)
(only endpoints,        (only domain libs +
 middleware, auth)       background jobs)
    │
    └── dotnet/aspnet:10.0     dotnet/runtime:10.0 ← lightweight!
```

### New Library Structure

#### 1. `packages/domain/SignalStack.Domain` (new)
**Moves from `apps/api`:**
- `Admin/*.cs` — domain types only (JobRunDocument, TradingCalendarDocument, IstTimeZone, PenetrationTestDocument, ChaosExerciseDocument — **exclude** endpoints and middleware)
- `Audit/AuditEventDocument.cs`
- `Execution/*.cs` — execution domain types
- `Portfolio/*.cs` — Holding, ManualAdjustmentDocument
- `Users/*.cs` — user domain types
- `Notifications/*.cs` — domain types only (NotificationDocument, DeliveryStatus, NotificationType, EntryFillData, ExitAdvisoryData — **exclude** endpoints)
- `Signals/SignalSubscriptionDocument.cs`

**SDK:** `Microsoft.NET.Sdk` (class library — no web dependency)

---

#### 2. `packages/storage/SignalStack.Storage` (new)
**Moves from `apps/api`:**
- `Admin/ITradingCalendarRepository.cs`, `MongoTradingCalendarRepository.cs`
- `Admin/IPenetrationTestRepository.cs`, `MongoPenetrationTestRepository.cs`
- `Admin/IChaosExerciseRepository.cs`, `MongoChaosExerciseRepository.cs`
- `Fyers/IFyersTokenRepository.cs`, `MongoFyersTokenRepository.cs`
- `Fyers/FyersTokenDocument.cs`
- `Historical/IOhlcvRepository.cs` (and SqlOhlcvRepository from wherever it lives)
- `Historical/ISymbolTableMapping.cs`, `SymbolTableMappingService.cs`
- `LedgerWriters/ITradeLedgerRepository.cs`, `MongoTradeLedgerRepository.cs`
- `Notifications/INotificationWriter.cs`, `NotificationWriter.cs`
- `Notifications/INotificationRepository.cs`, `MongoNotificationRepository.cs`
- `Notifications/NotificationExtensions.cs` (DI registration — not API-specific)
- `Portfolio/IManualAdjustmentRepository.cs`, `MongoManualAdjustmentRepository.cs`
- `Signals/ISignalSubscriptionRepository.cs`, `MongoSignalSubscriptionRepository.cs`
- `SysConfig/ISysConfigRepository.cs`, `MongoSysConfigRepository.cs`
- `Universe/*.cs` — all repositories and domain services (**exclude** endpoints)
- `Backtesting/IBacktestRepository.cs` (and InMemoryBacktestRepository if shared)
- `Backtesting/ExecutionModel.cs`

**References:** `packages/domain`
**SDK:** `Microsoft.NET.Sdk`

---

#### 3. `packages/signals/SignalStack.Signals` (new)
**Moves from `apps/api`:**
- `Backtesting/IEntrySignalEvaluator.cs`
- `Backtesting/MaCrossoverEvaluator.cs`
- `Signals/*.cs` — signal subscription logic (not document)

**References:** `packages/domain`, `packages/storage`
**SDK:** `Microsoft.NET.Sdk`

---

#### 4. `packages/notifications/SignalStack.Notifications` (new)
**Moves from `apps/api`:**
- `Notifications/NotificationTemplateBuilder.cs`
- `TelegramBot/TelegramBotService.cs`, `TelegramLinkingService.cs`
- `TelegramBot/ITelegramBotTokenStore.cs`
- Telegrams options, extensions (not endpoints)

**References:** `packages/domain`, `packages/storage`
**SDK:** `Microsoft.NET.Sdk`

---

#### 5. `packages/historical/SignalStack.Historical` (new)
**Moves from `apps/api`:**
- `Historical/Timeframe.cs`, `ITimeframeService.cs`, `TimeframeService.cs`
- `Historical/WeeklyCandleBoundary.cs`
- `Historical/IOhlcvRepository.cs`, `ISymbolTableMapping.cs`, `SymbolTableMappingService.cs`

**References:** `packages/domain`, `packages/storage`
**SDK:** `Microsoft.NET.Sdk`

---

### What Stays in the API Project

After extraction, `apps/api` retains only presentation-layer code:

| Folder | Contents |
|---|---|
| `Auth/` | Endpoints, middleware, JWT service, CSRF |
| `Sessions/` | Session middleware |
| `Pld/` | WebSocket connection manager, endpoints |
| `PhaseEnforcement/` | Phase middleware |
| `PrivacyRequest/` | Privacy endpoints, repo |
| `DataBreach/` | Data breach endpoints |
| `Push/` | Push event types |
| `TelegramBot/` | Telegram bot **endpoints** only |
| `Admin/*Endpoints.cs` | Admin endpoints |
| `Admin/ImpersonationMiddleware.cs` | Impersonation middleware |
| `Admin/PhaseGateService.cs` | Phase gate (API orchestration) |
| `Notifications/NotificationEndpoints.cs` | Notification endpoints |
| `Historical/ChartEndpoints.cs` | Chart endpoints |
| `Historical/HistoricalExtensions.cs` | Keep `InitializeHistoricalServicesAsync` (depends on `WebApplication`) |
| `Backtesting/BacktestEndpoints.cs` | Backtest endpoints |
| `Universe/*Endpoints.cs` | Universe endpoints |
| `Program.cs` | Entry point (thinned) |

**API's new csproj references:** Domain libraries + `Microsoft.NET.Sdk.Web` (unchanged Dockerfile)

---

### Worker After Refactor

```xml
<!-- apps/worker/SignalStack.Worker.csproj (after) -->
<ProjectReference Include="..\..\packages\config\SignalStack.Configuration\..." />
<ProjectReference Include="..\..\packages\market-data\SignalStack.MarketData\..." />
<ProjectReference Include="..\..\packages\migrations\SignalStack.Migrations\..." />
<ProjectReference Include="..\..\packages\migrations\SignalStack.SqlMigrations\..." />
<ProjectReference Include="..\..\packages\domain\SignalStack.Domain\..." />
<ProjectReference Include="..\..\packages\storage\SignalStack.Storage\..." />
<ProjectReference Include="..\..\packages\signals\SignalStack.Signals\..." />
<ProjectReference Include="..\..\packages\notifications\SignalStack.Notifications\..." />
<ProjectReference Include="..\..\packages\historical\SignalStack.Historical\..." />
<!-- NO reference to apps/api/SignalStack.Api.csproj -->
```

**Worker Dockerfile** switches to:
```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
```
No ASP.NET Core runtime needed.

---

## Migration Steps

The refactoring should be sequenced to minimize risk. Each step is independently verifiable:

### Phase A: Foundation (safe, no-op extractions)

1. **Create `packages/domain/SignalStack.Domain`**
   - Move pure type definitions (documents, enums, value objects)
   - No logic beyond constructors and simple properties
   - Update namespaces, fix all `using` statements

2. **Create `packages/storage/SignalStack.Storage`**
   - Move repository interfaces and MongoDB implementations
   - Reference `SignalStack.Domain`
   - Move DI registration extensions

### Phase B: Domain libraries

3. **Create `packages/historical/SignalStack.Historical`**
   - Move timeframe services, OHLCV abstractions
   - Reference `SignalStack.Domain` and `SignalStack.Storage`

4. **Create `packages/signals/SignalStack.Signals`**
   - Move evaluators, subscription logic

5. **Create `packages/notifications/SignalStack.Notifications`**
   - Move template builders, Telegram service

### Phase C: Wire up

6. **Update `apps/api/Program.cs` and csproj**
   - Remove moved files from API project
   - Add project references to new packages
   - Ensure API builds and all endpoint registrations still work

7. **Update `apps/worker/Program.cs` and csproj**
   - Remove reference to `apps/api/SignalStack.Api.csproj`
   - Add references to new packages
   - Fix using statements to new namespaces

8. **Update Worker Dockerfile**
   - Change base image to `dotnet/runtime:10.0`
   - Remove ASP.NET Core runtime dependency

### Phase D: Verify

9. **Build and test**
   - `dotnet build` on solution
   - API tests pass
   - Worker tests pass
   - Startup smoke test (local: `dotnet run` on both projects)

10. **CI/CD validation**
    - Both Dockerfiles build successfully
    - Both containers start without framework errors
    - Railway deploy passes health check

---

## Migration Stats (Estimated)

| Metric | Value |
|---|---|
| Files moved from `apps/api` | ~120 files |
| New package projects | 5 |
| Lines of code relocated | ~8,000-12,000 (est.) |
| Worker Docker image size reduction | ~100-150 MB |
| Worker csproj ProjectReferences removed | 1 (the API) |
| Worker csproj ProjectReferences added | 5 (domain libraries) |

---

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| **Namespace churn** — all moved files need new `namespace` declarations | Use file-scoped namespaces matching the new package name; bulk rename via `sed` or IDE |
| **API-specific dependencies** — some files in shared folders may reference ASP.NET Core types | Handle case-by-case: extract a base interface into the domain lib, keep the ASP.NET-dependent implementation in the API project |
| **Circular dependencies** — domain libs must not reference `apps/api` | Enforce with a build script or solution-level rule; keep dependency graph one-way |
| **Transient build breaks** — Worker tests reference API through Worker transitively | Worker tests reference Worker project; once Worker no longer references API, the transitive dependency breaks — Worker tests must add direct references to needed domain libs |
| **DI registration duplication** — registration extensions split between API and packages | Each package exposes its own `IServiceCollection` extension method; API's `Program.cs` calls all of them |
