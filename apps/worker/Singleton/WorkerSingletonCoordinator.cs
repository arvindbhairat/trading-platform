using Microsoft.Extensions.Options;
using SignalStack.Worker.Configuration;

namespace SignalStack.Worker.Singleton;

internal sealed class WorkerSingletonCoordinator : IWorkerSingletonCoordinator
{
  private readonly IWorkerInstanceIdentityProvider _identityProvider;
  private readonly IWorkerSingletonLeaseBackend _leaseBackend;
  private readonly ILogger<WorkerSingletonCoordinator> _logger;
  private readonly WorkerSingletonOptions _options;
  private bool _leaseHeld;

  public WorkerSingletonCoordinator(
    IWorkerInstanceIdentityProvider identityProvider,
    IWorkerSingletonLeaseBackend leaseBackend,
    ILogger<WorkerSingletonCoordinator> logger,
    IOptions<WorkerSingletonOptions> options)
  {
    _identityProvider = identityProvider;
    _leaseBackend = leaseBackend;
    _logger = logger;
    _options = options.Value;
  }

  public async Task<WorkerStartupDecision> AcquireStartupLeaseAsync(CancellationToken cancellationToken)
  {
    var instanceId = _identityProvider.GetInstanceId();
    var leaseTtl = TimeSpan.FromSeconds(_options.LeaseTtlSeconds);

    var acquireResult = await _leaseBackend.TryAcquireAsync(
      _options.LeaseKey,
      instanceId,
      leaseTtl,
      cancellationToken);

    switch (acquireResult.Outcome)
    {
      case WorkerLeaseAcquireOutcome.Acquired:
        _leaseHeld = true;
        _logger.LogInformation(
          "Worker singleton lease acquired for instance {InstanceId} on key {LeaseKey}.",
          instanceId,
          _options.LeaseKey);
        return WorkerStartupDecision.Run();

      case WorkerLeaseAcquireOutcome.Conflict:
        _logger.LogError(
          "singleton_violation: worker instance {InstanceId} detected active holder {HolderInstanceId} for lease {LeaseKey}.",
          instanceId,
          acquireResult.HolderInstanceId ?? "unknown",
          _options.LeaseKey);
        return WorkerStartupDecision.ExitWithFailure(acquireResult.HolderInstanceId);

      default:
        _logger.LogWarning(
          "singleton_lease_unavailable: worker instance {InstanceId} could not verify lease {LeaseKey}; proceeding without this enforcement layer. Reason={Reason}",
          instanceId,
          _options.LeaseKey,
          acquireResult.Reason ?? "unknown");
        return WorkerStartupDecision.Run();
    }
  }

  public async Task TryRefreshLeaseAsync(CancellationToken cancellationToken)
  {
    if (!_leaseHeld)
    {
      return;
    }

    var refreshResult = await _leaseBackend.TryRefreshAsync(
      _options.LeaseKey,
      _identityProvider.GetInstanceId(),
      TimeSpan.FromSeconds(_options.LeaseTtlSeconds),
      cancellationToken);

    if (refreshResult.Outcome == WorkerLeaseRefreshOutcome.Refreshed)
    {
      return;
    }

    _leaseHeld = false;

    if (refreshResult.Outcome == WorkerLeaseRefreshOutcome.LostLease)
    {
      _logger.LogError(
        "singleton_violation: worker instance {InstanceId} lost lease {LeaseKey} during refresh.",
        _identityProvider.GetInstanceId(),
        _options.LeaseKey);
      return;
    }

    _logger.LogWarning(
      "singleton_lease_unavailable: worker instance {InstanceId} could not refresh lease {LeaseKey}. Reason={Reason}",
      _identityProvider.GetInstanceId(),
      _options.LeaseKey,
      refreshResult.Reason ?? "unknown");
  }
}
