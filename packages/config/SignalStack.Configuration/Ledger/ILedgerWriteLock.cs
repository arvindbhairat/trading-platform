namespace SignalStack.Configuration.Ledger;

/// <summary>
/// Per-user distributed write lock for the trade ledger.
/// Every write path that modifies the trade ledger, triggers FIFO recomputation, or writes a portfolio
/// snapshot must acquire this lock before beginning work.  REQ-PORT-031 / REQ-PORT-031b.
/// </summary>
public interface ILedgerWriteLock
{
  /// <summary>
  /// Attempts to acquire the lock for the given user.
  /// Acquisition behaviour depends on <paramref name="mode"/>:
  /// <list type="bullet">
  ///   <item>TryNonBlocking — single try; background sync jobs skip the user and continue.</item>
  ///   <item>WaitFiveSeconds — user-initiated manual sync waits up to 5 seconds.</item>
  ///   <item>WaitThirtySeconds — admin rebuild / backfill waits up to 30 seconds.</item>
  /// </list>
  /// On success the returned <see cref="LedgerLockHandle"/> carries the fencing token required for
  /// MongoDB write filters (REQ-PORT-031b).  The caller must dispose the handle when done.
  /// </summary>
  Task<LedgerLockAcquireResult> AcquireAsync(
    string userId,
    LedgerAcquireMode mode,
    CancellationToken cancellationToken);
}
