using Microsoft.Extensions.Logging;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.AdminRebuild;

/// <summary>
/// Admin-triggered trade-ledger rebuild service (skeleton for P5-T8 lock wiring).
///
/// Acquires the per-user trade-ledger write lock in WaitThirtySeconds mode
/// (admin operation — willing to wait for a contested lock). Actual rebuild
/// logic will be wired when the admin rebuild workflow is implemented.
///
/// REQ-PORT-027: admin "Rebuild trade ledger from broker" operation.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// </summary>
public sealed class AdminRebuildService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly ILogger<AdminRebuildService> _logger;

    public AdminRebuildService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        ILogger<AdminRebuildService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a full trade-ledger rebuild for the given user.
    /// Waits up to 30 seconds for a contested lock (admin operation).
    /// </summary>
    public async Task ExecuteAsync(string userId, CancellationToken ct)
    {
        var acquireResult = await _ledgerLock.AcquireAsync(
            userId, LedgerAcquireMode.WaitThirtySeconds, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogWarning(
                "Admin rebuild: lock not acquired for user {UserId} ({Outcome}). " +
                "Admin should retry or check active sync operations.",
                userId, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            // P5-T8: lock acquired — full rebuild logic to be added.
            _logger.LogInformation(
                "Admin rebuild: lock acquired for user {UserId} (fence {FenceToken}). " +
                "Rebuild logic to be added in the admin rebuild task.",
                userId, handle.FencingToken);

            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
