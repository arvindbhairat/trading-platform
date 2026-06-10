namespace SignalStack.Worker.Singleton;

internal interface IWorkerSingletonLeaseBackend
{
  Task<WorkerLeaseAcquireResult> TryAcquireAsync(
    string leaseKey,
    string instanceId,
    TimeSpan leaseTtl,
    CancellationToken cancellationToken);

  Task<WorkerLeaseRefreshResult> TryRefreshAsync(
    string leaseKey,
    string instanceId,
    TimeSpan leaseTtl,
    CancellationToken cancellationToken);

  /// <summary>
  /// Atomically releases the lease if and only if it is still held by <paramref name="instanceId"/>.
  /// Called during graceful shutdown (SIGTERM) so the next instance can acquire immediately
  /// instead of waiting for lease TTL expiry.
  /// </summary>
  Task<bool> TryReleaseAsync(string leaseKey, string instanceId);
}
