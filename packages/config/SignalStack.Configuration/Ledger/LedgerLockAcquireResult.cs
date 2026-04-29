namespace SignalStack.Configuration.Ledger;

public sealed record LedgerLockAcquireResult(
  LedgerLockAcquireOutcome Outcome,
  LedgerLockHandle? Handle = null,
  string? Reason = null)
{
  public static LedgerLockAcquireResult Acquired(LedgerLockHandle handle) =>
    new(LedgerLockAcquireOutcome.Acquired, handle);

  public static LedgerLockAcquireResult Busy() =>
    new(LedgerLockAcquireOutcome.Busy);

  public static LedgerLockAcquireResult Unavailable(string reason) =>
    new(LedgerLockAcquireOutcome.Unavailable, Reason: reason);
}
