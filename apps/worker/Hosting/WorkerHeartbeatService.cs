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

  // Maximum retry attempts for lease acquisition on conflict.
  // With exponential backoff (2^retry seconds), 6 retries span ~126s,
  // comfortably covering the 60s lease TTL during overlapping deployments.
  private const int MaxLeaseRetryAttempts = 6;

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
      // Retry with exponential backoff — handles the overlapping-deployment
      // window where the previous instance still holds the Redis lease.
      for (var attempt = 1; attempt <= MaxLeaseRetryAttempts; attempt++)
      {
        var delaySeconds = Math.Min((int)Math.Pow(2, attempt), 64);
        _logger.LogWarning(
          "Singleton lease conflict (holder: {Holder}). " +
          "Retrying in {Delay}s (attempt {Attempt}/{MaxRetries})...",
          startupDecision.DetectedHolderInstanceId ?? "unknown",
          delaySeconds, attempt, MaxLeaseRetryAttempts);

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);

        startupDecision = await _singletonCoordinator.AcquireStartupLeaseAsync(stoppingToken);
        if (!startupDecision.ShouldExitWithFailure)
          break;
      }
    }

    if (startupDecision.ShouldExitWithFailure)
    {
      _logger.LogError(
        "Singleton lease could not be acquired after {MaxRetries} attempts. " +
        "Last holder: {Holder}. Exiting.",
        MaxLeaseRetryAttempts, startupDecision.DetectedHolderInstanceId ?? "unknown");

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
