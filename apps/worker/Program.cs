using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignalStack.Api.Backtesting;
using SignalStack.Api.Historical;
using SignalStack.Api.Signals;
using SignalStack.Api.Universe;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Configuration.Ledger;
using SignalStack.MarketData.Throttling;
using SignalStack.Migrations;
using SignalStack.Worker.Configuration;
using SignalStack.Worker.Hosting;
using SignalStack.Worker.Integrations.Fyers;
using SignalStack.Worker.Observability;
using SignalStack.Worker.Jobs.DataSync;
using SignalStack.Worker.Jobs.EodSignalRunner;
using SignalStack.Worker.Jobs.HistoricDataSeed;
using SignalStack.Api.Notifications;
using SignalStack.Worker.Rme;
using SignalStack.Worker.Singleton;
using SignalStack.Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.AddSignalStackBootstrapConfiguration(WorkerTelemetry.ServiceName);
builder.AddSignalStackTelemetry(WorkerTelemetry.ServiceName);

var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDb");
var mongoDatabaseName = builder.Configuration["MongoDB:DatabaseName"] ?? "signalstack";
builder.Services.AddMongoMigrations(mongoConnectionString, mongoDatabaseName);

builder.Services
  .AddOptions<WorkerSingletonOptions>()
  .BindConfiguration(WorkerSingletonOptions.SectionName)
  .ValidateDataAnnotations()
  .ValidateOnStart();

builder.Services.AddSingleton<IWorkerInstanceIdentityProvider, WorkerInstanceIdentityProvider>();
builder.Services.AddSingleton<IWorkerSingletonLeaseBackend, RedisWorkerSingletonLeaseBackend>();
builder.Services.AddSingleton<IWorkerSingletonCoordinator, WorkerSingletonCoordinator>();
builder.Services.AddHostedService<WorkerHeartbeatService>();

// Trade-ledger write lock primitives (REQ-PORT-031/031a/031b — writers wired in P5-T8)
builder.Services.AddLedgerWriteLock();

// RME per-position channel registry (ADR-0003, REQ-RME-CONC-001/004/005)
builder.Services
    .AddOptions<PositionChannelOptions>()
    .BindConfiguration(PositionChannelOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IRmeEventConsumer, NoOpRmeEventConsumer>();
builder.Services.AddSingleton<IPositionChannelRegistry, PositionChannelRegistry>();

// Sentinel startup check (REQ-CONFIG-010): fails fast if seeder was skipped.
builder.Services.AddSingleton<SeedVersionStartupGuard>();

// Symbol master suffix collision check (REQ-HIST-008a): fails fast if duplicate
// sql_table_name_suffix values exist in the symbol master.
builder.Services.AddSingleton<SymbolMasterCollisionGuard>();

// Universe management services: symbol master, sync health (noop fallback), probe.
builder.Services.AddUniverseManagement();

// Trading calendar management (REQ-CALENDAR-001..006) — used by SymbolProbeWorker
// and DataSyncWorker for session date resolution and DS run gating.
builder.Services.AddTradingCalendarManagement();

// Historical OHLCV data services — P3-T11 / REQ-HIST-001..011, REQ-HIST-010a.
// Registers ISymbolTableMapping (single shared mapping) and IOhlcvRepository (SQL Server).
// Required by HistoricDataSeed (HDS) for per-symbol table materialisation and resumable seeding.
var sqlConnectionString = builder.Configuration.GetConnectionString("SqlServer");
builder.Services.AddHistoricalServices(sqlConnectionString);

// FYERS MDP adapter + throttle layer (P3-T9 / REQ-RATE-003/004/011/012).
// Routes all FYERS API calls through the centralised throttling layer with
// per-second, per-minute, and per-day limits from sys_config.
builder.Services.AddFyersMarketDataProvider(builder.Configuration);

// Symbol Validity Probe worker (REQ-UNIV-021/021a/021b).
builder.Services.AddHostedService<SymbolProbeWorker>();

// HistoricDataSeed (HDS) job: per-symbol table materialisation + idempotent + resumable.
// Polls job_runs for pending HDS jobs, creates D_/W_/M_ tables atomically,
// fetches historical OHLCV from the MDP, and computes weekly/monthly aggregates.
// REQ-HIST-009/009a.
builder.Services.AddHistoricDataSeed(builder.Configuration);

// DataSync (DS) job: daily incremental OHLCV sync for post-market data refresh.
// Backfills the last N trading sessions (default 10), upserts weekly/monthly
// aggregates, re-checks admin token at every session boundary, and writes
// success markers for EODSR gating.
// REQ-MARKET-003/005/005a/006/007/009/013.
builder.Services.AddDataSync(builder.Configuration);

// EODSR sequencing gate (REQ-MARKET-007): EOD Signal Runner refuses to start
// without a DataSync EOD success marker for the target trading session.
// Registers IEodMarkerReader (reads DS markers from job_runs) and EodSequencingGate.
builder.Services.AddEodSequencingGate();

// Signal Subscription repository (REQ-STRAT-007a/007b, REQ-STRAT-017b).
// Required by EODSR for reading active subscription state.
builder.Services.AddSingleton<ISignalSubscriptionRepository, MongoSignalSubscriptionRepository>();

// Built-in Signal evaluators (REQ-STRAT-025): registered for discovery by SignalEvaluatorRegistry.
builder.Services.AddSingleton<IEntrySignalEvaluator, MaCrossoverEvaluator>();

// Notification collection schema + writer paths — P5-T3 / REQ-NOTIFY-006
// REQ-NOTIFY-004: synchronous persistence before Telegram delivery.
// REQ-NOTIFY-007: producers write to notifications collection; no Telegram dispatch here.
builder.Services.AddNotificationServices();

// EOD Signal Runner (EODSR): post-market evaluation of user-subscribed Signal types.
// REQ-STRAT-013/013a: single-pass per-symbol read + user-scoped evaluation.
// REQ-STRAT-027: atomic-run semantics with db-retry.
// REQ-STRAT-028: provenance fields on entry signals.
builder.Services.AddEodSignalRunner(builder.Configuration);

var host = builder.Build();

// ── Sentinel startup check (REQ-CONFIG-010) ─────────────────────────────
// Fail fast if the sys_config seeder has not been run.
var sentinelGuard = host.Services.GetRequiredService<SeedVersionStartupGuard>();
await sentinelGuard.VerifyAsync();

// ── Symbol master suffix collision check (REQ-HIST-008a) ─────────────────
// Fail fast if any duplicate sql_table_name_suffix values exist.
var collisionGuard = host.Services.GetRequiredService<SymbolMasterCollisionGuard>();
await collisionGuard.VerifyAsync();

// ── Initialise symbol-to-table-name mapping (REQ-HIST-011) ────────────────
// Load the ISymbolTableMapping cache from MongoDB symbol_master so that
// HistoricDataSeed and other jobs can resolve table names immediately.
var tableMapping = host.Services.GetRequiredService<ISymbolTableMapping>();
if (tableMapping is SymbolTableMappingService mappingSvc)
{
    await mappingSvc.InitializeAsync();
}

await host.RunAsync();

public partial class Program;
