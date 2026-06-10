namespace SignalStack.Worker.Singleton;

internal interface IWorkerSingletonCoordinator
{
  Task<WorkerStartupDecision> AcquireStartupLeaseAsync(CancellationToken cancellationToken);

  Task TryRefreshLeaseAsync(CancellationToken cancellationToken);

  /// <summary>
  /// Gracefully releases the lease during shutdown (SIGTERM) so the next instance
  /// can acquire it immediately instead of waiting for TTL expiry.
  /// Safe to call even if the lease was never acquired — it's a no-op.
  /// </summary>
  Task TryReleaseLeaseAsync();
}
