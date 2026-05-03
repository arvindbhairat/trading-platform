using Microsoft.Extensions.Logging;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Api.LedgerWriters;

/// <summary>
/// Manual adjustment service for trade-ledger corrections (REQ-RECON-003/004).
///
/// Acquires the per-user trade-ledger write lock in WaitThirtySeconds mode
/// (admin operation — willing to wait for a contested lock). On success,
/// records the adjustment entry in the trade ledger as an explicitly flagged
/// adjustment record with audit context.
///
/// REQ-RECON-003: manual corrections are recorded as explicit adjustment entries.
/// REQ-RECON-004: adjustment entries recorded against holding/position context.
/// REQ-PORT-014/015: seeded opening balance and adjustment entry types.
/// REQ-PORT-031/031a/031b: lock acquisition + fencing-token guard + version increment.
/// </summary>
public sealed class ManualAdjustmentService
{
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly TradeIngestionService _ingestionService;
    private readonly ILogger<ManualAdjustmentService> _logger;

    public ManualAdjustmentService(
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        TradeIngestionService ingestionService,
        ILogger<ManualAdjustmentService> logger)
    {
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Records a manual adjustment entry in the trade ledger.
    /// The adjustment is ingested as an explicit adjustment record with audit
    /// context and the standard Nifty-500 filter and dedup rules.
    /// </summary>
    /// <param name="userId">Platform user ID.</param>
    /// <param name="adjustmentTrade">The adjustment trade entry.</param>
    /// <param name="adjustmentContext">
    /// Contextual reference for the adjustment — e.g. the affected holding symbol
    /// or <c>manual_adjustments</c> collection document ID. REQ-RECON-004.
    /// </param>
    /// <param name="notes">Optional adjustment reason notes.</param>
    public async Task ExecuteAsync(
        string userId,
        RawTrade adjustmentTrade,
        string adjustmentContext,
        string? notes = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(adjustmentTrade);

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
            _logger.LogInformation(
                "Manual adjustment: recording adjustment for user {UserId} " +
                "symbol {Symbol} (fence {FenceToken}).",
                userId, adjustmentTrade.Symbol, handle.FencingToken);

            var result = await _ingestionService.IngestAsync(
                userId,
                [adjustmentTrade],
                handle.FencingToken,
                SyncSource.ManualAdjustment,
                DateTime.UtcNow,
                isAdjustment: true,
                adjustmentContext: adjustmentContext,
                ct: ct);

            if (result.Inserted == 0)
            {
                _logger.LogWarning(
                    "Manual adjustment: trade_id {TradeId} already exists for user {UserId}. " +
                    "Adjustment not recorded (duplicate).",
                    adjustmentTrade.TradeId, userId);
            }

            // REQ-PORT-031a: always increment on successful write.
            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }

    /// <summary>
    /// Records a seeded opening balance entry for an incomplete-history holding
    /// (REQ-PORT-015). The entry is recorded with <c>SyncSource.SeededBalance</c>
    /// and an <c>isAdjustment</c> flag so it is clearly distinguished from
    /// broker-synced trades.
    /// </summary>
    /// <param name="seedingContext">
    /// The holding context — typically the symbol or a reference ID.
    /// </param>
    public async Task SeedOpeningBalanceAsync(
        string userId,
        RawTrade seedTrade,
        string seedingContext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(seedTrade);

        var acquireResult = await _ledgerLock.AcquireAsync(
            userId, LedgerAcquireMode.WaitThirtySeconds, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogWarning(
                "Seed balance: lock not acquired for user {UserId} ({Outcome}). " +
                "Admin should retry or check active sync operations.",
                userId, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            _logger.LogInformation(
                "Seed balance: recording opening balance for user {UserId} " +
                "symbol {Symbol} (fence {FenceToken}).",
                userId, seedTrade.Symbol, handle.FencingToken);

            await _ingestionService.IngestAsync(
                userId,
                [seedTrade],
                handle.FencingToken,
                SyncSource.SeededBalance,
                DateTime.UtcNow,
                isAdjustment: true,
                adjustmentContext: seedingContext,
                ct: ct);

            await _snapshotVersionHelper.IncrementAsync(userId, ct);
        }
        catch
        {
            throw;
        }
    }
}
