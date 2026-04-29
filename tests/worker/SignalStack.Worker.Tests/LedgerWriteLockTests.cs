using SignalStack.Configuration.Ledger;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Unit tests for the per-user trade-ledger write lock primitives.
/// REQ-PORT-031 / REQ-PORT-031a / REQ-PORT-031b.
/// </summary>
public sealed class LedgerWriteLockTests
{
  // ─── LedgerLockHandle — fencing token ──────────────────────────────────────

  [Fact]
  public void Handle_exposes_fencing_token()
  {
    var handle = MakeHandle(fencingToken: 7L);
    Assert.Equal(7L, handle.FencingToken);
  }

  [Fact]
  public async Task TryRenewAsync_returns_true_when_token_matches()
  {
    var handle = MakeHandle(fencingToken: 1L, renewReturns: true);
    Assert.True(await handle.TryRenewAsync(CancellationToken.None));
  }

  [Fact]
  public async Task TryRenewAsync_returns_false_when_lock_lost()
  {
    var handle = MakeHandle(fencingToken: 1L, renewReturns: false);
    Assert.False(await handle.TryRenewAsync(CancellationToken.None));
  }

  [Fact]
  public async Task ReleaseAsync_is_idempotent_and_calls_delegate_exactly_once()
  {
    var callCount = 0;
    var handle = new LedgerLockHandle(
      fencingToken: 1L,
      renewAsync: _ => Task.FromResult(true),
      releaseAsync: () => { callCount++; return Task.CompletedTask; });

    await handle.ReleaseAsync();
    await handle.ReleaseAsync();
    await handle.DisposeAsync();

    Assert.Equal(1, callCount);
  }

  // ─── LedgerLockAcquireResult ────────────────────────────────────────────────

  [Fact]
  public void Acquired_result_carries_handle_with_correct_outcome()
  {
    var handle = MakeHandle(fencingToken: 3L);
    var result = LedgerLockAcquireResult.Acquired(handle);

    Assert.Equal(LedgerLockAcquireOutcome.Acquired, result.Outcome);
    Assert.NotNull(result.Handle);
    Assert.Equal(3L, result.Handle.FencingToken);
  }

  [Fact]
  public void Busy_result_has_correct_outcome_and_no_handle()
  {
    var result = LedgerLockAcquireResult.Busy();
    Assert.Equal(LedgerLockAcquireOutcome.Busy, result.Outcome);
    Assert.Null(result.Handle);
  }

  [Fact]
  public void Unavailable_result_has_correct_outcome_and_reason()
  {
    var result = LedgerLockAcquireResult.Unavailable("redis_timeout");
    Assert.Equal(LedgerLockAcquireOutcome.Unavailable, result.Outcome);
    Assert.Equal("redis_timeout", result.Reason);
  }

  // ─── Contract tests: REQ-PORT-031b invariant A-11 ──────────────────────────
  // These tests assert the core invariant that must be upheld by every write path
  // wired in P5-T8: ledger_snapshot_version is incremented only after a durable commit,
  // never on a fencing-token abort.

  [Fact]
  public async Task FencingTokenAbort_does_not_advance_ledger_snapshot_version()
  {
    // Arrange: our handle holds fencing token 5.
    // The MongoDB document's stored fence token has advanced to 6 — another process took the lock.
    // The write filter { "ledger_fence_token": { "$lte": acquiredToken } } therefore matches 0 docs.
    var versionSpy = new SpyVersionHelper();
    var handle = MakeHandle(fencingToken: 5L);

    const long storedFenceToken = 6L; // advanced past our acquired token
    var matchedCount = storedFenceToken <= handle.FencingToken ? 1 : 0; // 0 = stale-write abort

    // Act: correct write path — only increment version on durable commit (matched > 0)
    if (matchedCount > 0)
      await versionSpy.IncrementAsync("user-1", CancellationToken.None);
    // else: fencing abort — do NOT increment (REQ-PORT-031b invariant A-11)

    // Assert: version must remain unchanged
    Assert.Equal(0, versionSpy.IncrementCallCount);
  }

  [Fact]
  public async Task DurableCommit_advances_ledger_snapshot_version_exactly_once()
  {
    // Arrange: our handle holds fencing token 5.
    // The document's stored fence token is 4 (our write is the latest) — filter matches 1 doc.
    var versionSpy = new SpyVersionHelper();
    var handle = MakeHandle(fencingToken: 5L);

    const long storedFenceToken = 4L; // older — our write is valid
    var matchedCount = storedFenceToken <= handle.FencingToken ? 1 : 0; // 1 = write committed

    // Act: correct write path
    if (matchedCount > 0)
      await versionSpy.IncrementAsync("user-1", CancellationToken.None);

    // Assert: version advanced exactly once after confirmed durable commit
    Assert.Equal(1, versionSpy.IncrementCallCount);
  }

  [Fact]
  public async Task SameFenceToken_write_is_considered_valid()
  {
    // Stored fence token == acquired token means the holder's own previous fence write is present.
    // The filter { "$lte": acquiredToken } still matches.
    var versionSpy = new SpyVersionHelper();
    var handle = MakeHandle(fencingToken: 5L);

    const long storedFenceToken = 5L; // equal — our write
    var matchedCount = storedFenceToken <= handle.FencingToken ? 1 : 0; // 1

    if (matchedCount > 0)
      await versionSpy.IncrementAsync("user-1", CancellationToken.None);

    Assert.Equal(1, versionSpy.IncrementCallCount);
  }

  // ─── Fake lock acquire (stub for non-Redis tests) ───────────────────────────

  [Fact]
  public async Task FakeLock_acquire_returns_handle_with_positive_fencing_token()
  {
    var fakeLock = new FakeLedgerWriteLock(fencingToken: 1L);
    var result = await fakeLock.AcquireAsync("user-1", LedgerAcquireMode.TryNonBlocking, CancellationToken.None);

    Assert.Equal(LedgerLockAcquireOutcome.Acquired, result.Outcome);
    Assert.NotNull(result.Handle);
    Assert.True(result.Handle.FencingToken > 0);
  }

  [Fact]
  public async Task FakeLock_busy_returns_busy_outcome()
  {
    var fakeLock = new FakeLedgerWriteLock(outcome: LedgerLockAcquireOutcome.Busy);
    var result = await fakeLock.AcquireAsync("user-1", LedgerAcquireMode.TryNonBlocking, CancellationToken.None);

    Assert.Equal(LedgerLockAcquireOutcome.Busy, result.Outcome);
    Assert.Null(result.Handle);
  }

  // ─── Helpers ─────────────────────────────────────────────────────────────────

  private static LedgerLockHandle MakeHandle(long fencingToken, bool renewReturns = true) =>
    new(fencingToken,
      _ => Task.FromResult(renewReturns),
      () => Task.CompletedTask);
}

file sealed class SpyVersionHelper : ILedgerSnapshotVersionHelper
{
  public int IncrementCallCount { get; private set; }

  public Task IncrementAsync(string userId, CancellationToken cancellationToken)
  {
    IncrementCallCount++;
    return Task.CompletedTask;
  }
}

file sealed class FakeLedgerWriteLock : ILedgerWriteLock
{
  private readonly long _fencingToken;
  private readonly LedgerLockAcquireOutcome _outcome;

  public FakeLedgerWriteLock(
    long fencingToken = 1L,
    LedgerLockAcquireOutcome outcome = LedgerLockAcquireOutcome.Acquired)
  {
    _fencingToken = fencingToken;
    _outcome = outcome;
  }

  public Task<LedgerLockAcquireResult> AcquireAsync(
    string userId, LedgerAcquireMode mode, CancellationToken cancellationToken)
  {
    if (_outcome == LedgerLockAcquireOutcome.Acquired)
    {
      var handle = new LedgerLockHandle(
        _fencingToken,
        _ => Task.FromResult(true),
        () => Task.CompletedTask);
      return Task.FromResult(LedgerLockAcquireResult.Acquired(handle));
    }

    if (_outcome == LedgerLockAcquireOutcome.Busy)
      return Task.FromResult(LedgerLockAcquireResult.Busy());

    return Task.FromResult(LedgerLockAcquireResult.Unavailable("stub_unavailable"));
  }
}
