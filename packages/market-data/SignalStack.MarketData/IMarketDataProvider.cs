namespace SignalStack.MarketData;

/// <summary>
/// Transport-agnostic market data provider interface.
///
/// All market data access — historical, live quotes, and streaming — flows through
/// this interface. No job, service, or domain layer may call a specific provider's
/// API directly. The interface deliberately surfaces no transport semantics
/// so that adapters can switch transport without changing any consumer code.
/// See ADR-0005.
///
/// Implementations must be independently testable against the same contract.
/// The active implementation is resolved at startup from admin configuration
/// (<c>market_data.provider.active</c>) per REQ-MKTPROV-005.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Gets a human-readable name for the provider (e.g. "FYERS", "TrueData", "GDF").
    /// Used for logging, metrics, and diagnostic surfacing only — never for
    /// conditional branching in domain or service code.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Fetch historical daily OHLCV candles for a symbol over a date range.
    ///
    /// Batched: implementations may make multiple API calls to satisfy the range.
    /// Results must be ordered by date ascending. Empty list if no data is
    /// available for the range.
    ///
    /// Per REQ-MKTPROV-004, this is a minimum required operation.
    /// </summary>
    Task<IReadOnlyList<OhlcvRecord>> FetchHistoricalOhlcvAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch the latest real-time or delayed quote for a symbol.
    ///
    /// Returns null if the symbol is unrecognised, not traded, or the provider
    /// returns an error for this symbol. Implementations must not throw for
    /// unrecognised symbols; return null and log at Debug level.
    ///
    /// Per REQ-MKTPROV-004, this is a minimum required operation.
    /// </summary>
    Task<QuoteRecord?> GetLatestQuoteAsync(
        string symbol,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to a push-adapter stream of live price updates for one or more
    /// symbols. The returned sequence yields <see cref="QuoteRecord"/> values as
    /// they arrive from the provider.
    ///
    /// Transport-agnostic: the implementation may poll REST, maintain a persistent
    /// connection, or use any other transport. Consumers receive <see cref="QuoteRecord"/>
    /// values regardless of the underlying transport. See ADR-0005.
    ///
    /// The sequence completes when the cancellation token is signalled or the
    /// provider indicates the subscription has ended. Each <see cref="QuoteRecord"/>
    /// carries a <see cref="QuoteRecord.TimestampUtc"/> so consumers can assess
    /// freshness independently of delivery timing.
    /// </summary>
    IAsyncEnumerable<QuoteRecord> SubscribeToLivePrices(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default);
}
