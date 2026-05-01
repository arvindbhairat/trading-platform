using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignalStack.Api.Universe;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Configuration.Ledger;
using SignalStack.Migrations;
using SignalStack.Worker.Configuration;
using SignalStack.Worker.Hosting;
using SignalStack.Worker.Observability;
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

// Symbol Validity Probe worker (REQ-UNIV-021/021a/021b).
builder.Services.AddHostedService<SymbolProbeWorker>();

var host = builder.Build();

// ── Sentinel startup check (REQ-CONFIG-010) ─────────────────────────────
// Fail fast if the sys_config seeder has not been run.
var sentinelGuard = host.Services.GetRequiredService<SeedVersionStartupGuard>();
await sentinelGuard.VerifyAsync();

// ── Symbol master suffix collision check (REQ-HIST-008a) ─────────────────
// Fail fast if any duplicate sql_table_name_suffix values exist.
var collisionGuard = host.Services.GetRequiredService<SymbolMasterCollisionGuard>();
await collisionGuard.VerifyAsync();

await host.RunAsync();

public partial class Program;
