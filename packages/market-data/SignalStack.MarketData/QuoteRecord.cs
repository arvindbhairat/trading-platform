namespace SignalStack.MarketData;

/// <summary>
/// A single quote snapshot returned by <see cref="IMarketDataProvider"/>.
///
/// Provider-agnostic: every adapter maps its provider's response shape into this
/// record so that consumers (LMDS, chart page, advisory panel) see a uniform
/// representation.
/// </summary>
public record QuoteRecord(
    string Symbol,
    decimal LastPrice,
    decimal Change,
    decimal ChangePercent,
    DateTime TimestampUtc,
    bool IsDelayed);
