namespace SignalStack.Worker.Singleton;

internal interface IWorkerSingletonCoordinator
{
  Task<WorkerStartupDecision> AcquireStartupLeaseAsync(CancellationToken cancellationToken);

  Task TryRefreshLeaseAsync(CancellationToken cancellationToken);
}
