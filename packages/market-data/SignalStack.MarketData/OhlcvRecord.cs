namespace SignalStack.MarketData;

/// <summary>
/// A single daily OHLCV data point returned by <see cref="IMarketDataProvider"/>.
///
/// Provider-agnostic: every adapter maps its provider's response shape into this
/// record so that all downstream consumers (charting, backtesting, EODSR, etc.)
/// see a uniform representation.
/// </summary>
public record OhlcvRecord(
    string Symbol,
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
