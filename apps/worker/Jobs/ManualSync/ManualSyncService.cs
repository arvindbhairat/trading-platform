using Microsoft.Extensions.Logging;
using SignalStack.Storage.LedgerWriters;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.ManualSync;

/// <summary>
/// User-triggered manual account sync service (REQ-PORT-021).
///
/// Acquires the per-user trade-ledger write lock in WaitFiveSeconds mode
/// (user-initiated — willing to wait briefly for a contested lock). On success,
/// ingests the provided trades into the trade ledger under the lock's
/// fencing-token guard, then increments <c>ledger_snapshot_version</c>.
///
/// REQ-PORT-021:  user-triggered manual sync, rate-limited.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// REQ-PORT-005a: non-Nifty-500 exclusion applied during ingestion.
/// REQ-PORT-023:  trade_id dedup.
/// </summary>
public sealed class ManualSyncService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly TradeIngestionService _ingestionService;
    private readonly ILogger<ManualSyncService> _logger;

    public ManualSyncService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        TradeIngestionService ingestionService,
        ILogger<ManualSyncService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a manual account sync for the given user.
    /// </summary>
    /// <param name="userId">Platform user ID.</param>
    /// <param name="rawTrades">
    /// Trades fetched from FYERS by the caller.
    /// Pass an empty list if no new trades are available.
    /// </param>
    public async Task ExecuteAsync(
        string userId,
        IReadOnlyList<RawTrade> rawTrades,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rawTrades);

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
            if (rawTrades.Count > 0)
            {
                _logger.LogInformation(
                    "Manual sync: ingesting {Count} trades for user {UserId} (fence {FenceToken}).",
                    rawTrades.Count, userId, handle.FencingToken);

                var result = await _ingestionService.IngestAsync(
                    userId, rawTrades, handle.FencingToken, SyncSource.Intraday,
                    DateTime.UtcNow, ct: ct);

                _logger.LogInformation(
                    "Manual sync: ingestion result for user {UserId} — " +
                    "{Inserted} inserted, {Duplicates} duplicates, {Excluded} excluded.",
                    userId, result.Inserted, result.SkippedDuplicates, result.SkippedNonNifty500);
            }
            else
            {
                _logger.LogTrace(
                    "Manual sync: no trades to ingest for user {UserId} (fence {FenceToken}).",
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
