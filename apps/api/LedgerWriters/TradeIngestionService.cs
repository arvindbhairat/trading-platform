using Microsoft.Extensions.Logging;
using SignalStack.Api.Universe;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Api.LedgerWriters;

/// <summary>
/// Central trade ingestion pipeline shared by all sync paths.
///
/// Accepts raw trade data from any source (FYERS API, manual entry, backfill),
/// validates each trade against the symbol master for Nifty-500 membership,
/// deduplicates by <c>trade_id</c>, and writes to the <c>trade_ledger</c>
/// collection under the caller's fencing-token guard.
///
/// REQ-PORT-005a: non-Nifty-500 symbols are silently ignored.
/// REQ-PORT-023:  trade_id dedup (idempotent).
/// REQ-RECON-003/004: adjustment entries supported via callers that set
///                    <c>isAdjustment</c> / <c>adjustmentContext</c>.
/// </summary>
public sealed class TradeIngestionService
{
    private readonly ITradeLedgerRepository _repository;
    private readonly ISymbolMasterRepository _symbolMaster;
    private readonly ILogger<TradeIngestionService> _logger;

    public TradeIngestionService(
        ITradeLedgerRepository repository,
        ISymbolMasterRepository symbolMaster,
        ILogger<TradeIngestionService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _symbolMaster = symbolMaster ?? throw new ArgumentNullException(nameof(symbolMaster));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ingests a batch of raw trades into the trade ledger.
    ///
    /// Processing order per trade:
    /// 1. Symbol master check — trades for symbols not in the Nifty 500
    ///    universe are silently excluded (REQ-PORT-005a).
    /// 2. Duplicate check — trades whose <c>trade_id</c> already exists
    ///    for this user are silently skipped (REQ-PORT-023).
    /// 3. Write — surviving trades are written with the caller's fencing
    ///    token and sync source.
    ///
    /// The caller must have already acquired the per-user trade-ledger
    /// write lock and performed the fencing-token guard write before
    /// calling this method.
    /// </summary>
    /// <param name="userId">Platform user ID.</param>
    /// <param name="rawTrades">Raw trade data from the sync source.</param>
    /// <param name="fencingToken">The fence token from the active lock handle.</param>
    /// <param name="syncSource">
    /// One of <see cref="SyncSource.TradeHistory"/>, <see cref="SyncSource.Intraday"/>,
    /// <see cref="SyncSource.ManualAdjustment"/>, or <see cref="SyncSource.SeededBalance"/>.
    /// </param>
    /// <param name="ingestedAt">Timestamp to record as ingestion time. Use <c>DateTime.UtcNow</c>.</param>
    /// <param name="isAdjustment">True when this is a manual adjustment entry (REQ-RECON-003).</param>
    /// <param name="adjustmentContext">Optional reference for adjustment context (REQ-RECON-004).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<IngestionResult> IngestAsync(
        string userId,
        IReadOnlyList<RawTrade> rawTrades,
        long fencingToken,
        string syncSource,
        DateTime ingestedAt,
        bool isAdjustment = false,
        string? adjustmentContext = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rawTrades);

        if (rawTrades.Count == 0)
            return new IngestionResult(0, 0, 0);

        // ── Step 1: Load active symbol master for Nifty-500 check ──────────
        var activeSymbols = await _symbolMaster.GetAllAsync(archived: false, ct);
        var activeSymbolSet = activeSymbols
            .Select(s => s.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toIngest = new List<TradeLedgerDocument>(rawTrades.Count);
        var excluded = 0;

        foreach (var raw in rawTrades)
        {
            // REQ-PORT-005a: silently skip non-Nifty-500 symbols.
            if (!activeSymbolSet.Contains(raw.Symbol))
            {
                _logger.LogTrace(
                    "Trade excluded (non-Nifty-500): user={UserId} symbol={Symbol} trade_id={TradeId}.",
                    userId, raw.Symbol, raw.TradeId);
                excluded++;
                continue;
            }

            toIngest.Add(new TradeLedgerDocument
            {
                TradeId = raw.TradeId,
                UserId = userId,
                Symbol = raw.Symbol,
                Side = raw.Side,
                Quantity = raw.Quantity,
                Price = raw.Price,
                TradeAt = raw.TradeAtUtc,
                OrderId = raw.OrderId,
                SyncSource = syncSource,
                IngestedAt = ingestedAt,
                LedgerFenceToken = fencingToken,
                IsAdjustment = isAdjustment,
                AdjustmentContext = adjustmentContext,
            });
        }

        // ── Step 2: Bulk-insert with dedup ─────────────────────────────────
        var bulkResult = await _repository.InsertTradesAsync(toIngest, ct);

        _logger.LogInformation(
            "Trade ingestion complete for user {UserId}: " +
            "{Inserted} inserted, {SkippedDuplicates} duplicates, " +
            "{Excluded} excluded (non-Nifty-500).",
            userId, bulkResult.Inserted, bulkResult.SkippedDuplicates, excluded);

        return new IngestionResult(bulkResult.Inserted, bulkResult.SkippedDuplicates, excluded);
    }
}
