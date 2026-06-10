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
  private readonly IWorkerInstanceIdentityProvider _identityProvider;

  // Maximum retry attempts for lease acquisition on conflict.
  // With exponential backoff (2^retry capped at 64 seconds), 8 retries span ~254s (~4 min),
  // covering the 60s lease TTL plus the Railway deploy overlap window where the old
  // instance still holds the lease.
  // With explicit lease release on graceful shutdown (StopAsync), the overlap window
  // is reduced to nearly zero — the old instance deletes the Redis key before exiting,
  // and the new instance acquires it immediately on its next retry.
  private const int MaxLeaseRetryAttempts = 8;

  public WorkerHeartbeatService(
    IHostApplicationLifetime applicationLifetime,
    ILogger<WorkerHeartbeatService> logger,
    IOptions<WorkerSingletonOptions> options,
    IWorkerSingletonCoordinator singletonCoordinator,
    IWorkerInstanceIdentityProvider identityProvider)
  {
    _applicationLifetime = applicationLifetime;
    _logger = logger;
    _options = options.Value;
    _singletonCoordinator = singletonCoordinator;
    _identityProvider = identityProvider;
  }

  /// <summary>
  /// Called by the host on graceful shutdown (SIGTERM). Releases the Redis lease
  /// so the next worker instance can acquire it immediately, eliminating the
  /// deployment overlap window.
  ///
  /// Sequence: Railway sends SIGTERM → Host calls StopAsync on all hosted services
  /// → base.StopAsync cancels the ExecuteAsync loop → we release the lease → host
  /// waits for all services to stop → process exits.
  ///
  /// The release is atomic (Lua DEL only if instance matches) and safe to call even
  /// if the lease was already lost or never acquired.
  /// </summary>
  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    // Let the heartbeat loop exit first (cancels stoppingToken, waits for ExecuteAsync to return).
    await base.StopAsync(cancellationToken);

    // Now release the lease so the next instance can acquire immediately.
    await _singletonCoordinator.TryReleaseLeaseAsync();
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var startupActivity = WorkerTelemetry.ActivitySource.StartActivity("worker.startup");
    startupActivity?.SetTag("singleton.lease_key", _options.LeaseKey);
    startupActivity?.SetTag("singleton.instance_id", _identityProvider.GetInstanceId());
    startupActivity?.SetTag("singleton.lease_ttl_seconds", _options.LeaseTtlSeconds);
    startupActivity?.SetTag("singleton.heartbeat_interval_seconds", _options.HeartbeatIntervalSeconds);

    var startupDecision = await _singletonCoordinator.AcquireStartupLeaseAsync(stoppingToken);
    startupActivity?.SetTag("singleton.initial_acquired", !startupDecision.ShouldExitWithFailure);
    startupActivity?.SetTag("singleton.initial_holder", startupDecision.DetectedHolderInstanceId ?? "unknown");
    var retryCount = 0;

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
        retryCount = attempt;
      }
    }

    startupActivity?.SetTag("singleton.retry_attempts", retryCount);
    startupActivity?.SetTag("singleton.acquired", !startupDecision.ShouldExitWithFailure);

    if (startupDecision.ShouldExitWithFailure)
    {
      startupActivity?.SetStatus(ActivityStatusCode.Error, "Lease acquisition failed after retries");
      startupActivity?.AddEvent(new ActivityEvent("singleton.lease_failed",
          tags: new ActivityTagsCollection
          {
              ["last_holder"] = startupDecision.DetectedHolderInstanceId ?? "unknown",
              ["max_retries"] = MaxLeaseRetryAttempts
          }));

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
        using var refreshActivity = WorkerTelemetry.ActivitySource.StartActivity("worker.lease_refresh");
        try
        {
          await _singletonCoordinator.TryRefreshLeaseAsync(stoppingToken);
          refreshActivity?.SetTag("singleton.refresh_successful", true);
        }
        catch (Exception ex)
        {
          refreshActivity?.SetTag("singleton.refresh_successful", false);
          refreshActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
          refreshActivity?.AddException(ex);
          _logger.LogWarning(ex, "Singleton lease refresh failed.");
        }
        WorkerTelemetry.LeaseRefreshDurationMilliseconds.Record(Stopwatch.GetElapsedTime(leaseRefreshStartedAt).TotalMilliseconds);
        lastLeaseRefreshUtc = DateTimeOffset.UtcNow;
      }

      WorkerTelemetry.HeartbeatCounter.Add(1);
      _logger.LogTrace("SignalStack.Worker heartbeat.");
    }
  }
}
