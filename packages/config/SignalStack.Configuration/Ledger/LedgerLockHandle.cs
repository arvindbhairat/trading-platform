namespace SignalStack.Configuration.Ledger;

/// <summary>
/// Represents a successfully acquired per-user trade-ledger write lock (REQ-PORT-031).
/// Carries the fencing token used to guard MongoDB writes against stale-lock eviction (REQ-PORT-031b).
/// Dispose/release must be called after the protected operation completes or aborts.
/// </summary>
public sealed class LedgerLockHandle : IAsyncDisposable
{
  private readonly Func<CancellationToken, Task<bool>> _renewAsync;
  private readonly Func<Task> _releaseAsync;
  private int _disposed;

  public LedgerLockHandle(
    long fencingToken,
    Func<CancellationToken, Task<bool>> renewAsync,
    Func<Task> releaseAsync)
  {
    FencingToken = fencingToken;
    _renewAsync = renewAsync;
    _releaseAsync = releaseAsync;
  }

  /// <summary>
  /// Monotonically increasing fencing token (from Redis INCR on ledger:write:fence:{userId}).
  /// Every MongoDB write under this lock must include a filter { "ledger_fence_token": { "$lte": FencingToken } }.
  /// A zero-match result indicates a stale-write abort; the operation must not increment ledger_snapshot_version.
  /// REQ-PORT-031b(b).
  /// </summary>
  public long FencingToken { get; }

  /// <summary>
  /// Renews the lock TTL. Returns false if the lock was lost (another process evicted and re-acquired it).
  /// A false return means the current operation must abort; do not increment ledger_snapshot_version.
  /// REQ-PORT-031.
  /// </summary>
  public Task<bool> TryRenewAsync(CancellationToken cancellationToken)
    => _renewAsync(cancellationToken);

  /// <summary>Releases the lock. Idempotent — safe to call more than once.</summary>
  public Task ReleaseAsync()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 0)
      return _releaseAsync();
    return Task.CompletedTask;
  }

  public ValueTask DisposeAsync() => new(ReleaseAsync());
}
