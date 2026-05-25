using Microsoft.Extensions.Logging;
using SignalStack.Storage.LedgerWriters;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.AdminRebuild;

/// <summary>
/// Admin-triggered trade-ledger rebuild service (REQ-PORT-027).
///
/// Acquires the per-user trade-ledger write lock in WaitThirtySeconds mode
/// (admin operation — willing to wait for a contested lock). On success,
/// replays all provided trades through the ingestion pipeline and increments
/// <c>ledger_snapshot_version</c>.
///
/// REQ-PORT-027:  admin "Rebuild trade ledger from broker" operation.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// REQ-PORT-005a: non-Nifty-500 exclusion.
/// REQ-PORT-023:  trade_id dedup.
/// </summary>
public sealed class AdminRebuildService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly TradeIngestionService _ingestionService;
    private readonly ILogger<AdminRebuildService> _logger;

    public AdminRebuildService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        TradeIngestionService ingestionService,
        ILogger<AdminRebuildService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a full trade-ledger rebuild for the given user.
    /// Waits up to 30 seconds for a contested lock (admin operation).
    /// </summary>
    /// <param name="userId">Platform user ID.</param>
    /// <param name="rawTrades">
    /// All trades for the user replayed from the FYERS Trade History API.
    /// The ingestion pipeline handles dedup and Nifty-500 filtering.
    /// </param>
    public async Task ExecuteAsync(
        string userId,
        IReadOnlyList<RawTrade> rawTrades,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rawTrades);

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
            if (rawTrades.Count > 0)
            {
                _logger.LogInformation(
                    "Admin rebuild: replaying {Count} trades for user {UserId} (fence {FenceToken}).",
                    rawTrades.Count, userId, handle.FencingToken);

                var result = await _ingestionService.IngestAsync(
                    userId, rawTrades, handle.FencingToken, SyncSource.TradeHistory,
                    DateTime.UtcNow, ct: ct);

                _logger.LogInformation(
                    "Admin rebuild: ingestion result for user {UserId} — " +
                    "{Inserted} inserted, {Duplicates} duplicates, {Excluded} excluded.",
                    userId, result.Inserted, result.SkippedDuplicates, result.SkippedNonNifty500);
            }
            else
            {
                _logger.LogInformation(
                    "Admin rebuild: no trades to ingest for user {UserId} (fence {FenceToken}). " +
                    "Trade ledger will be empty for this user.",
                    userId, handle.FencingToken);
            }

            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
