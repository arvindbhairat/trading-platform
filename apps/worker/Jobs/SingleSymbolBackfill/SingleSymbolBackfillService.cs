using Microsoft.Extensions.Logging;
using SignalStack.Storage.LedgerWriters;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.SingleSymbolBackfill;

/// <summary>
/// Single-symbol trade-ledger backfill service (REQ-PORT-030).
///
/// Acquires the per-user trade-ledger write lock in WaitThirtySeconds mode
/// (admin operation — willing to wait for a contested lock). On success,
/// ingests the backfilled trades for the specified symbol and increments
/// <c>ledger_snapshot_version</c>.
///
/// REQ-PORT-030:  single-symbol backfill from broker.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// REQ-PORT-005a: non-Nifty-500 exclusion (symbol must be in the universe).
/// REQ-PORT-023:  trade_id dedup.
/// </summary>
public sealed class SingleSymbolBackfillService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly TradeIngestionService _ingestionService;
    private readonly ILogger<SingleSymbolBackfillService> _logger;

    public SingleSymbolBackfillService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        TradeIngestionService ingestionService,
        ILogger<SingleSymbolBackfillService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a single-symbol backfill for the given user.
    /// Waits up to 30 seconds for a contested lock (admin operation).
    /// </summary>
    /// <param name="userId">Platform user ID.</param>
    /// <param name="symbol">The NSE symbol to backfill.</param>
    /// <param name="rawTrades">
    /// Historical trades for the symbol from the FYERS Trade History API.
    /// </param>
    public async Task ExecuteAsync(
        string userId,
        string symbol,
        IReadOnlyList<RawTrade> rawTrades,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rawTrades);

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
            if (rawTrades.Count > 0)
            {
                _logger.LogInformation(
                    "Symbol backfill: ingesting {Count} trades for user {UserId} " +
                    "symbol {Symbol} (fence {FenceToken}).",
                    rawTrades.Count, userId, symbol, handle.FencingToken);

                var result = await _ingestionService.IngestAsync(
                    userId, rawTrades, handle.FencingToken, SyncSource.TradeHistory,
                    DateTime.UtcNow, ct: ct);

                _logger.LogInformation(
                    "Symbol backfill: ingestion result for user {UserId} symbol {Symbol} — " +
                    "{Inserted} inserted, {Duplicates} duplicates, {Excluded} excluded.",
                    userId, symbol, result.Inserted, result.SkippedDuplicates, result.SkippedNonNifty500);
            }
            else
            {
                _logger.LogInformation(
                    "Symbol backfill: no trades to ingest for user {UserId} symbol {Symbol} " +
                    "(fence {FenceToken}).",
                    userId, symbol, handle.FencingToken);
            }

            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
