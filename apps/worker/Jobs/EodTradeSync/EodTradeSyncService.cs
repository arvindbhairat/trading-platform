using Microsoft.Extensions.Logging;
using SignalStack.Storage.LedgerWriters;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.EodTradeSync;

/// <summary>
/// EOD trade-ledger sync service (REQ-PORT-020).
///
/// Acquires the per-user trade-ledger write lock in TryNonBlocking mode
/// during post-market trade ledger sync. On success, ingests end-of-day
/// trades into the trade ledger under the lock's fencing-token guard,
/// then increments <c>ledger_snapshot_version</c>.
///
/// REQ-PORT-020:  EOD sync of trade ledger after market close.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// REQ-PORT-005a: non-Nifty-500 exclusion applied during ingestion.
/// REQ-PORT-023:  trade_id dedup.
/// </summary>
public sealed class EodTradeSyncService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly TradeIngestionService _ingestionService;
    private readonly ILogger<EodTradeSyncService> _logger;

    public EodTradeSyncService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        TradeIngestionService ingestionService,
        ILogger<EodTradeSyncService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes an EOD trade-ledger sync for the given user.
    /// </summary>
    /// <param name="userId">Platform user ID.</param>
    /// <param name="rawTrades">
    /// Trades fetched from FYERS Trade History API by the caller.
    /// Pass an empty list if no new trades are available.
    /// </param>
    public async Task ExecuteAsync(
        string userId,
        IReadOnlyList<RawTrade> rawTrades,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rawTrades);

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
            if (rawTrades.Count > 0)
            {
                _logger.LogInformation(
                    "EOD trade sync: ingesting {Count} trades for user {UserId} (fence {FenceToken}).",
                    rawTrades.Count, userId, handle.FencingToken);

                var result = await _ingestionService.IngestAsync(
                    userId, rawTrades, handle.FencingToken, SyncSource.TradeHistory,
                    DateTime.UtcNow, ct: ct);

                _logger.LogInformation(
                    "EOD trade sync: ingestion result for user {UserId} — " +
                    "{Inserted} inserted, {Duplicates} duplicates, {Excluded} excluded.",
                    userId, result.Inserted, result.SkippedDuplicates, result.SkippedNonNifty500);
            }
            else
            {
                _logger.LogTrace(
                    "EOD trade sync: no trades to ingest for user {UserId} (fence {FenceToken}).",
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
