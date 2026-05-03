using Microsoft.Extensions.Logging;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Api.LedgerWriters;

/// <summary>
/// On-login account sync service (REQ-PORT-018).
///
/// Acquires the per-user trade-ledger write lock in TryNonBlocking mode
/// when a user logs in and their FYERS token is valid. On success, ingests
/// any pending trades into the trade ledger under the lock's fencing-token
/// guard, then increments <c>ledger_snapshot_version</c>.
///
/// REQ-PORT-018:  initial / re-auth FYERS account sync on login.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// REQ-PORT-005a: non-Nifty-500 exclusion applied during ingestion.
/// REQ-PORT-023:  trade_id dedup.
/// </summary>
public sealed class OnLoginSyncService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly TradeIngestionService _ingestionService;
    private readonly ILogger<OnLoginSyncService> _logger;

    public OnLoginSyncService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        TradeIngestionService ingestionService,
        ILogger<OnLoginSyncService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes an on-login account sync for the given user.
    /// Acquires the lock, ingests the provided trades, increments the snapshot version.
    /// </summary>
    /// <param name="userId">Platform user ID.</param>
    /// <param name="rawTrades">
    /// Trades fetched from FYERS by the caller. Pass an empty list if no new trades
    /// are available.
    /// </param>
    /// <param name="syncSource">
    /// The sync source identifier — typically <see cref="SyncSource.TradeHistory"/>
    /// for historical backfill or <see cref="SyncSource.Intraday"/> for current-day sync.
    /// </param>
    public async Task ExecuteAsync(
        string userId,
        IReadOnlyList<RawTrade> rawTrades,
        string syncSource,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rawTrades);

        var acquireResult = await _ledgerLock.AcquireAsync(
            userId, LedgerAcquireMode.TryNonBlocking, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogTrace(
                "On-login sync: lock not acquired for user {UserId} ({Outcome}). " +
                "Background sync will pick up the trades.",
                userId, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            if (rawTrades.Count > 0)
            {
                _logger.LogInformation(
                    "On-login sync: ingesting {Count} trades for user {UserId} (fence {FenceToken}).",
                    rawTrades.Count, userId, handle.FencingToken);

                var result = await _ingestionService.IngestAsync(
                    userId, rawTrades, handle.FencingToken, syncSource,
                    DateTime.UtcNow, ct: ct);

                _logger.LogInformation(
                    "On-login sync: ingestion result for user {UserId} — " +
                    "{Inserted} inserted, {Duplicates} duplicates, {Excluded} excluded.",
                    userId, result.Inserted, result.SkippedDuplicates, result.SkippedNonNifty500);
            }
            else
            {
                _logger.LogTrace(
                    "On-login sync: no trades to ingest for user {UserId} (fence {FenceToken}).",
                    userId, handle.FencingToken);
            }

            // REQ-PORT-031a: increment after all writes committed.
            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
