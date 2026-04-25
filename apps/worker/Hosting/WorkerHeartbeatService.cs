using System.Diagnostics;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Configuration;
using SignalStack.Worker.Observability;
using SignalStack.Worker.Singleton;

namespace SignalStack.Worker.Hosting;

internal sealed class WorkerHeartbeatService : BackgroundService
{
  private readonly IHostApplicationLifetime _applicationLifetime;
  private readonly ILogger<WorkerHeartbeatService> _logger;
  private readonly WorkerSingletonOptions _options;
  private readonly IWorkerSingletonCoordinator _singletonCoordinator;

  public WorkerHeartbeatService(
    IHostApplicationLifetime applicationLifetime,
    ILogger<WorkerHeartbeatService> logger,
    IOptions<WorkerSingletonOptions> options,
    IWorkerSingletonCoordinator singletonCoordinator)
  {
    _applicationLifetime = applicationLifetime;
    _logger = logger;
    _options = options.Value;
    _singletonCoordinator = singletonCoordinator;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var startupActivity = WorkerTelemetry.ActivitySource.StartActivity("worker.startup");
    var startupDecision = await _singletonCoordinator.AcquireStartupLeaseAsync(stoppingToken);

    if (startupDecision.ShouldExitWithFailure)
    {
      Environment.ExitCode = 1;
      _applicationLifetime.StopApplication();
      return;
    }

    _logger.LogInformation("SignalStack.Worker started.");
    var lastLeaseRefreshUtc = DateTimeOffset.UtcNow;

    while (!stoppingToken.IsCancellationRequested)
    {
      await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds), stoppingToken);

      if (DateTimeOffset.UtcNow - lastLeaseRefreshUtc >= TimeSpan.FromSeconds(_options.RefreshIntervalSeconds))
      {
        var leaseRefreshStartedAt = Stopwatch.GetTimestamp();
        await _singletonCoordinator.TryRefreshLeaseAsync(stoppingToken);
        WorkerTelemetry.LeaseRefreshDurationMilliseconds.Record(Stopwatch.GetElapsedTime(leaseRefreshStartedAt).TotalMilliseconds);
        lastLeaseRefreshUtc = DateTimeOffset.UtcNow;
      }

      WorkerTelemetry.HeartbeatCounter.Add(1);
      _logger.LogInformation("SignalStack.Worker heartbeat.");
    }
  }
}
