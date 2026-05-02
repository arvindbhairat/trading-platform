using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SignalStack.MarketData;

namespace SignalStack.Worker.Integrations.TrueData;

/// <summary>
/// Stub TrueData implementation of <see cref="IMarketDataProvider"/>.
///
/// Returns canned OHLCV and quote data for cross-provider swap testing
/// per REQ-MARKET-002d. This adapter validates that the MDP abstraction
/// boundary holds under a provider swap — consumers receive data through
/// the same <see cref="IMarketDataProvider"/> interface regardless of
/// which provider is active.
///
/// Production consumers (DataSync, EODSR, chart layer) require no code
/// changes when the active provider switches because all access flows
/// through the shared interface.
/// </summary>
public sealed class TrueDataMarketDataProvider : IMarketDataProvider
{
    private readonly ILogger<TrueDataMarketDataProvider> _logger;

    /// <summary>Canned OHLCV data used for swap testing.</summary>
    internal static readonly IReadOnlyList<OhlcvRecord> CannedOhlcv = new List<OhlcvRecord>
    {
        new(Symbol: "RELIANCE", Date: new DateOnly(2025, 1, 2),  Open: 2450.00m, High: 2485.50m, Low: 2435.00m,  Close: 2472.80m,  Volume: 15_000_000L),
        new(Symbol: "RELIANCE", Date: new DateOnly(2025, 1, 3),  Open: 2475.00m, High: 2500.00m, Low: 2465.00m,  Close: 2490.10m,  Volume: 12_500_000L),
        new(Symbol: "RELIANCE", Date: new DateOnly(2025, 1, 6),  Open: 2492.00m, High: 2515.00m, Low: 2480.00m,  Close: 2505.60m,  Volume: 14_200_000L),
        new(Symbol: "RELIANCE", Date: new DateOnly(2025, 1, 7),  Open: 2508.00m, High: 2530.00m, Low: 2495.00m,  Close: 2522.90m,  Volume: 11_800_000L),
        new(Symbol: "RELIANCE", Date: new DateOnly(2025, 1, 8),  Open: 2520.00m, High: 2545.00m, Low: 2510.00m,  Close: 2535.40m,  Volume: 13_600_000L),
    };

    /// <summary>Canned quote data used for swap testing.</summary>
    internal static readonly QuoteRecord CannedQuote = new(
        Symbol: "RELIANCE",
        LastPrice: 2535.40m,
        Change: 12.50m,
        ChangePercent: 0.50m,
        TimestampUtc: new DateTime(2025, 1, 8, 9, 30, 0, DateTimeKind.Utc),
        IsDelayed: false);

    public TrueDataMarketDataProvider(ILogger<TrueDataMarketDataProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "TrueData (Stub)";

    /// <summary>
    /// Returns canned OHLCV for symbols that match the test data,
    /// or an empty list for unknown symbols.
    /// </summary>
    public Task<IReadOnlyList<OhlcvRecord>> FetchHistoricalOhlcvAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        _logger.LogDebug(
            "TrueData stub: FetchHistoricalOhlcvAsync({Symbol}, {From}, {To})",
            symbol, fromDate, toDate);

        var filtered = CannedOhlcv
            .Where(r => r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Date >= fromDate && r.Date <= toDate)
            .OrderBy(r => r.Date)
            .ToList() as IReadOnlyList<OhlcvRecord>;

        if (filtered.Count == 0)
        {
            _logger.LogInformation(
                "TrueData stub: no canned data for {Symbol} in range {From}..{To}.",
                symbol, fromDate, toDate);
        }

        return Task.FromResult(filtered);
    }

    /// <summary>
    /// Returns the canned quote for symbols that match the test data,
    /// or null for unknown symbols.
    /// </summary>
    public Task<QuoteRecord?> GetLatestQuoteAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        _logger.LogDebug(
            "TrueData stub: GetLatestQuoteAsync({Symbol})", symbol);

        if (CannedOhlcv.Any(r => r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult<QuoteRecord?>(CannedQuote with { Symbol = symbol.ToUpperInvariant() });
        }

        _logger.LogInformation(
            "TrueData stub: no canned quote for {Symbol}.", symbol);

        return Task.FromResult<QuoteRecord?>(null);
    }

    /// <summary>
    /// Returns canned quote records as an async-enumerable stream.
    /// </summary>
    public async IAsyncEnumerable<QuoteRecord> SubscribeToLivePrices(
        IEnumerable<string> symbols,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        _logger.LogInformation(
            "TrueData stub: SubscribeToLivePrices starting (canned stream).");

        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (CannedOhlcv.Any(r => r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
            {
                yield return CannedQuote with { Symbol = symbol.ToUpperInvariant() };
            }

            // Small delay to simulate streaming cadence
            try
            {
                await Task.Delay(10, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
