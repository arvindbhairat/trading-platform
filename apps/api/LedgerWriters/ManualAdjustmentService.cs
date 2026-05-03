using Microsoft.Extensions.Logging;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Api.LedgerWriters;

/// <summary>
/// Manual adjustment service for trade-ledger corrections (skeleton for P5-T8 lock wiring).
///
/// Acquires the per-user trade-ledger write lock in WaitThirtySeconds mode
/// (admin operation — willing to wait for a contested lock). Actual adjustment
/// logic (REQ-PORT-014/015, REQ-RECON-003/004) will be wired when the manual
/// adjustment workflow is implemented.
///
/// REQ-PORT-014/015: manual adjustment entry types.
/// REQ-RECON-003/004: reconciliation adjustment recording.
/// U-8: manual adjustment lock acquisition path.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// </summary>
public sealed class ManualAdjustmentService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly ILogger<ManualAdjustmentService> _logger;

    public ManualAdjustmentService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        ILogger<ManualAdjustmentService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a manual trade-ledger adjustment for the given user.
    /// Waits up to 30 seconds for a contested lock (admin operation).
    /// </summary>
    public async Task ExecuteAsync(string userId, CancellationToken ct)
    {
        var acquireResult = await _ledgerLock.AcquireAsync(
            userId, LedgerAcquireMode.WaitThirtySeconds, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogWarning(
                "Manual adjustment: lock not acquired for user {UserId} ({Outcome}). " +
                "Admin should retry or check active sync operations.",
                userId, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            // P5-T8: lock acquired — actual adjustment logic to be added.
            _logger.LogInformation(
                "Manual adjustment: lock acquired for user {UserId} (fence {FenceToken}). " +
                "Adjustment logic to be added in the manual adjustment task.",
                userId, handle.FencingToken);

            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
