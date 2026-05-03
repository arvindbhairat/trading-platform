namespace SignalStack.Configuration.Ledger;

/// <summary>
/// Raw trade data received from any source (FYERS API, manual entry, backfill).
/// This is the input contract for <c>TradeIngestionService</c>. The ingestion
/// pipeline validates, deduplicates, filters, and writes these to the trade ledger.
/// </summary>
public sealed record RawTrade(
    string TradeId,
    string Symbol,
    string Side,
    int Quantity,
    decimal Price,
    DateTime TradeAtUtc,
    string? OrderId);

/// <summary>
/// Result of a trade ingestion operation, returned by <c>TradeIngestionService</c>.
/// </summary>
public sealed record IngestionResult(
    int Inserted,
    int SkippedDuplicates,
    int SkippedNonNifty500);

/// <summary>
/// Standard sync source identifiers for the <c>SyncSource</c> field on
/// <see cref="TradeLedgerDocument"/>.
/// </summary>
public static class SyncSource
{
    /// <summary>Trades retrieved from the FYERS Trade History API (historical lookup).</summary>
    public const string TradeHistory = "trade_history";

    /// <summary>Trades retrieved from current-day FYERS transactions (intraday sync).</summary>
    public const string Intraday = "intraday";

    /// <summary>Manual correction or seeded opening balance entry (REQ-RECON-003, REQ-PORT-015).</summary>
    public const string ManualAdjustment = "manual_adjustment";

    /// <summary>Seeded opening balance for an incomplete-history holding (REQ-PORT-015).</summary>
    public const string SeededBalance = "seeded_balance";
}
