using Microsoft.Extensions.Logging;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Api.LedgerWriters;

/// <summary>
/// On-login account sync service (skeleton for P5-T8 lock wiring).
///
/// Acquires the per-user trade-ledger write lock in TryNonBlocking mode
/// when a user logs in and their FYERS token is valid. Actual sync logic
/// (positions, orders, trades, holdings) will be wired in P5-T9.
///
/// REQ-PORT-018: initial FYERS account sync triggered on login.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// </summary>
public sealed class OnLoginSyncService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly ILogger<OnLoginSyncService> _logger;

    public OnLoginSyncService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        ILogger<OnLoginSyncService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Triggers an on-login account sync for the given user.
    /// </summary>
    public async Task ExecuteAsync(string userId, CancellationToken ct)
    {
        var acquireResult = await _ledgerLock.AcquireAsync(
            userId, LedgerAcquireMode.TryNonBlocking, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogTrace(
                "On-login sync: lock not acquired for user {UserId} ({Outcome}). " +
                "Background sync will pick up the trade.",
                userId, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            // P5-T8: lock acquired — actual sync logic will be added in P5-T9.
            _logger.LogInformation(
                "On-login sync: lock acquired for user {UserId} (fence {FenceToken}). " +
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
