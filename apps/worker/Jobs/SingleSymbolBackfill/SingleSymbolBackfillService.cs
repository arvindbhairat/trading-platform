using Microsoft.Extensions.Logging;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.SingleSymbolBackfill;

/// <summary>
/// Single-symbol trade-ledger backfill service (skeleton for P5-T8 lock wiring).
///
/// Acquires the per-user trade-ledger write lock in WaitThirtySeconds mode
/// (admin operation — willing to wait for a contested lock). Actual backfill
/// logic will be wired when the single-symbol backfill workflow is implemented.
///
/// REQ-PORT-030: single-symbol backfill from broker.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// </summary>
public sealed class SingleSymbolBackfillService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly ILogger<SingleSymbolBackfillService> _logger;

    public SingleSymbolBackfillService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        ILogger<SingleSymbolBackfillService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a single-symbol backfill for the given user.
    /// Waits up to 30 seconds for a contested lock (admin operation).
    /// </summary>
    public async Task ExecuteAsync(string userId, string symbol, CancellationToken ct)
    {
        var acquireResult = await _ledgerLock.AcquireAsync(
            userId, LedgerAcquireMode.WaitThirtySeconds, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogWarning(
                "Symbol backfill: lock not acquired for user {UserId} " +
                "on symbol {Symbol} ({Outcome}). Admin should retry.",
                userId, symbol, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            // P5-T8: lock acquired — actual backfill logic to be added.
            _logger.LogInformation(
                "Symbol backfill: lock acquired for user {UserId} symbol {Symbol} " +
                "(fence {FenceToken}). Backfill logic to be added.",
                userId, symbol, handle.FencingToken);

            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
