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
}
