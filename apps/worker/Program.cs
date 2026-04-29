using Microsoft.Extensions.Hosting;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Configuration.Ledger;
using SignalStack.Migrations;
using SignalStack.Worker.Configuration;
using SignalStack.Worker.Hosting;
using SignalStack.Worker.Observability;
using SignalStack.Worker.Rme;
using SignalStack.Worker.Singleton;

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

var host = builder.Build();
await host.RunAsync();

public partial class Program;
