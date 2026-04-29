using Microsoft.Extensions.Hosting;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Migrations;
using SignalStack.Worker.Configuration;
using SignalStack.Worker.Hosting;
using SignalStack.Worker.Observability;
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

var host = builder.Build();
await host.RunAsync();

public partial class Program;
