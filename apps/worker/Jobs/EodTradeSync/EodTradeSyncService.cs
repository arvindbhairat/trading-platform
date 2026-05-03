using Microsoft.Extensions.Logging;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.EodTradeSync;

/// <summary>
/// EOD trade-ledger sync service (skeleton for P5-T8 lock wiring).
///
/// Acquires the per-user trade-ledger write lock in TryNonBlocking mode
/// during post-market trade ledger sync. Actual sync logic (positions,
/// orders, trades, holdings) will be wired in P5-T9.
///
/// REQ-PORT-020: EOD sync of trade ledger after market close.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// </summary>
public sealed class EodTradeSyncService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly ILogger<EodTradeSyncService> _logger;

    public EodTradeSyncService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        ILogger<EodTradeSyncService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes an EOD trade-ledger sync for the given user.
    /// Lock wiring verified — actual sync logic to be added in P5-T9.
    /// </summary>
    public async Task ExecuteAsync(string userId, CancellationToken ct)
    {
        var acquireResult = await _ledgerLock.AcquireAsync(
            userId, LedgerAcquireMode.TryNonBlocking, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogTrace(
                "EOD trade sync: lock not acquired for user {UserId} ({Outcome}). Skipping.",
                userId, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            // P5-T8: lock acquired — actual sync writes will be added in P5-T9.
            // For now, the fence token guard and version increment demonstrate
            // the wiring is complete.
            _logger.LogInformation(
                "EOD trade sync: lock acquired for user {UserId} (fence {FenceToken}). " +
                "Sync logic deferred to P5-T9.",
                userId, handle.FencingToken);

            // REQ-PORT-031a: increment must happen after durable commit.
            // Placeholder: increment when actual sync logic is present.
            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
