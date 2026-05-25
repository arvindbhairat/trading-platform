using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Configuration.Ledger;
using SignalStack.Domain.Admin;
using SignalStack.Domain.Signals;
using SignalStack.Historical;
using SignalStack.MarketData.Throttling;
using SignalStack.Migrations;
using SignalStack.Signals.Backtesting;
using SignalStack.Storage.Admin;
using SignalStack.Storage.Backtesting;
using SignalStack.Storage.Historical;
using SignalStack.Storage.Notifications;
using SignalStack.Storage.Signals;
using SignalStack.Storage.Universe;
using SignalStack.Worker.Configuration;
using SignalStack.Worker.Hosting;
using SignalStack.Worker.Integrations.Fyers;
using SignalStack.Worker.Observability;
using SignalStack.Worker.Jobs.DataSync;
using SignalStack.Worker.Jobs.EodSignalRunner;
using SignalStack.Worker.Jobs.HistoricDataSeed;
using SignalStack.Worker.Jobs.AccountSync;
using SignalStack.Worker.Jobs.NotificationDelivery;
using SignalStack.Worker.Jobs.EodTradeSync;
using SignalStack.Worker.Jobs.ManualSync;
using SignalStack.Worker.Jobs.AdminRebuild;
using SignalStack.Worker.Jobs.SingleSymbolBackfill;
using SignalStack.Worker.Jobs.LiveMarketScan;
using SignalStack.Worker.Jobs.AdminFyersTokenCheck;
using SignalStack.Worker.Jobs.SloBreachMonitor;
using SignalStack.Worker.Push;
using SignalStack.Worker.Rme;
using SignalStack.Worker.Singleton;
using SignalStack.Worker.Workers;

// Load local .env file from repo root for development connection strings.
DotNetEnv.Env.Load("../../.env");

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

// Trade-ledger write lock primitives (REQ-PORT-031/031a/031b)
builder.Services.AddLedgerWriteLock();

// Trade-ledger repository and ingestion pipeline (P5-T9 — REQ-PORT-005a, REQ-PORT-023, REQ-RECON-001..004)
builder.Services.AddTradeLedgerServices();

// RME per-position channel registry (ADR-0003, REQ-RME-CONC-001/004/005)
builder.Services
    .AddOptions<PositionChannelOptions>()
    .BindConfiguration(PositionChannelOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
// P6-T3: RME event consumer with lifecycle state-machine validation.
// Replaces NoOpRmeEventConsumer from P6-T2 now that the transition
// validator (ITransitionValidator) is registered via AddRmeModule.
builder.Services.AddSingleton<IRmeEventConsumer, RmeEventConsumer>();
builder.Services.AddSingleton<IPositionChannelRegistry, PositionChannelRegistry>();

// RME module skeleton — sizing / stop / advisory interfaces (P6-T2, REQ-RME-001..005)
builder.Services.AddRmeModule(builder.Configuration);

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
builder.Services.AddSharedTokenHealthService();

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

// Notification Delivery Job (NDJ) — P5-T4 / REQ-NOTIFY-008/019/022a
// Sole Telegram dispatcher for all notification types. Handles Retry-After,
// dirty-link auto-disable, and global-disable break-glass.
builder.Services.AddNotificationDelivery(builder.Configuration);

// SLO Breach Alert Routing — P8-T16 / REQ-SLO-008
// Routes SLO breach events to admin notifications with per-SLO cooldown
// suppression and metric emission (slo.breach_notification_fired).
builder.Services.AddSloBreachMonitor(builder.Configuration);

// Live Account Data Sync (LADS) — P5-T7 / REQ-PORT-019/021a
// Intraday per-user account sync during NSE market hours. Tracks
// consecutive cycle aborts with graduated suspension (warn at 2, suspend at 3).
builder.Services.AddAccountSync(builder.Configuration);

// Live Market Data Scan (LMDS) — P5-T12 / REQ-STOP-006/006a/006b/006c
// Continuous market-hours price + level monitoring across all open positions.
// Polls at configurable interval, evaluates stop/add/reduce/trailing-stop levels,
// enqueues PriceLevelBreachedEvent to the per-position channel registry.
// REQ-SLO-004: cycle-time capacity warning. REQ-PLC-004: circuit-clear detection.
builder.Services.AddLiveMarketScan(builder.Configuration);

// Admin FYERS Token Validity Check (P5-T14 / REQ-NOTIFY-022, REQ-NOTIFY-022b)
// Pre-market check at 08:30 IST + daily check at 15:00 IST on NSE trading days.
// Fires admin_fyers_token_expiry_warning notification if token is absent or invalid.
builder.Services.AddAdminFyersTokenCheck(builder.Configuration);

// Push event publisher for real-time WebSocket fan-out (P5-T13 / REQ-NFR-013)
// Publishes events to Redis pub/sub so the API can deliver them to connected
// portal clients. REQ-DATA-006a(c)(i): degrades gracefully on Redis failure.
builder.Services.AddPushEventPublisher();

// Trade-ledger writer services (P5-T8 lock wiring — REQ-PORT-031/031a/031b)
// Skeleton services that acquire the per-user write lock; logic wired in P5-T9+.
builder.Services.AddSingleton<EodTradeSyncService>();
builder.Services.AddSingleton<ManualSyncService>();
builder.Services.AddSingleton<AdminRebuildService>();
builder.Services.AddSingleton<SingleSymbolBackfillService>();

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
