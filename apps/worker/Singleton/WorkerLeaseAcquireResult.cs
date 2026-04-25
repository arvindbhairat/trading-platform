namespace SignalStack.Worker.Singleton;

internal enum WorkerLeaseAcquireOutcome
{
  Acquired,
  Conflict,
  Unavailable
}

internal sealed record WorkerLeaseAcquireResult(
  WorkerLeaseAcquireOutcome Outcome,
  string? HolderInstanceId = null,
  string? Reason = null)
{
  public static WorkerLeaseAcquireResult Acquired() => new(WorkerLeaseAcquireOutcome.Acquired);

  public static WorkerLeaseAcquireResult Conflict(string? holderInstanceId) =>
    new(WorkerLeaseAcquireOutcome.Conflict, holderInstanceId);

  public static WorkerLeaseAcquireResult Unavailable(string reason) =>
    new(WorkerLeaseAcquireOutcome.Unavailable, Reason: reason);
}
