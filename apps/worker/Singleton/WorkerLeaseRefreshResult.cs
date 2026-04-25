namespace SignalStack.Worker.Singleton;

internal enum WorkerLeaseRefreshOutcome
{
  Refreshed,
  LostLease,
  Unavailable
}

internal sealed record WorkerLeaseRefreshResult(WorkerLeaseRefreshOutcome Outcome, string? Reason = null)
{
  public static WorkerLeaseRefreshResult Refreshed() => new(WorkerLeaseRefreshOutcome.Refreshed);

  public static WorkerLeaseRefreshResult LostLease() => new(WorkerLeaseRefreshOutcome.LostLease);

  public static WorkerLeaseRefreshResult Unavailable(string reason) =>
    new(WorkerLeaseRefreshOutcome.Unavailable, reason);
}
