using Microsoft.Extensions.Logging;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.ManualSync;

/// <summary>
/// User-triggered manual account sync service (skeleton for P5-T8 lock wiring).
///
/// Acquires the per-user trade-ledger write lock in WaitFiveSeconds mode
/// (user-initiated — willing to wait briefly for a contested lock). Actual
/// sync logic will be wired in P5-T9.
///
/// REQ-PORT-021: user-triggered manual sync, rate-limited.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// </summary>
public sealed class ManualSyncService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly ILogger<ManualSyncService> _logger;

    public ManualSyncService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        ILogger<ManualSyncService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a manual account sync for the given user.
    /// Waits up to 5 seconds for a contested lock (user-initiated).
    /// </summary>
    public async Task ExecuteAsync(string userId, CancellationToken ct)
    {
        var acquireResult = await _ledgerLock.AcquireAsync(
            userId, LedgerAcquireMode.WaitFiveSeconds, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogWarning(
                "Manual sync: lock not acquired for user {UserId} ({Outcome}). " +
                "User-facing retry message needed at call site.",
                userId, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            // P5-T8: lock acquired — actual sync writes will be added in P5-T9.
            _logger.LogInformation(
                "Manual sync: lock acquired for user {UserId} (fence {FenceToken}). " +
                "Sync logic deferred to P5-T9.",
                userId, handle.FencingToken);

            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
