using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SignalStack.MarketData;
using SignalStack.MarketData.Throttling;

namespace SignalStack.Worker.Integrations.Fyers;

/// <summary>
/// FYERS implementation of <see cref="IMarketDataProvider"/>.
///
/// Routes all FYERS API v3 calls through <see cref="IMarketDataThrottle"/> to
/// enforce per-second, per-minute, and per-day rate limits. Maps FYERS response
/// shapes to provider-agnostic <see cref="OhlcvRecord"/> and <see cref="QuoteRecord"/>.
///
/// Authentication: Bearer token provided via <c>accessTokenProvider</c> delegate.
/// In production, this should read from the admin FYERS token store (Key Vault).
///
/// Per ADR-0005, the MDP interface is transport-agnostic. This adapter uses
/// REST for all operations including <see cref="SubscribeToLivePrices"/> (REST polling)
/// because the FYERS Data WebSocket is browser-tier only per REQ-MARKET-002b.
/// </summary>
public sealed class FyersMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly IMarketDataThrottle _throttle;
    private readonly Func<Task<string?>> _accessTokenProvider;
    private readonly ILogger<FyersMarketDataProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public FyersMarketDataProvider(
        HttpClient httpClient,
        IMarketDataThrottle throttle,
        Func<Task<string?>> accessTokenProvider,
        ILogger<FyersMarketDataProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "FYERS";

    public async Task<IReadOnlyList<OhlcvRecord>> FetchHistoricalOhlcvAsync(
        string symbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var fyersSymbol = FyersSymbolFormatter.ToFyersFormat(symbol);

        _logger.LogDebug(
            "Fetching historical OHLCV for {Symbol} (FYERS: {FyersSymbol}) from {From} to {To}.",
            symbol, fyersSymbol, fromDate, toDate);

        var result = await _throttle.ExecuteAsync<IReadOnlyList<OhlcvRecord>>(async ct =>
        {
            var token = await GetTokenAsync(ct);
            if (token is null)
            {
                _logger.LogError("Cannot fetch historical data: FYERS access token is unavailable.");
                return Array.Empty<OhlcvRecord>();
            }

            var requestBody = new
            {
                symbol = fyersSymbol,
                resolution = "D",
                date_format = "1",
                range_from = fromDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                range_to = toDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                cont_flag = "1",
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{FyersApiEndpoints.BaseUrl}{FyersApiEndpoints.History}")
            {
                Content = JsonContent.Create(requestBody),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<FyersHistoryResponse>(JsonOptions, ct);

            if (body?.S != "ok" || body.Data?.Candles is null)
            {
                _logger.LogWarning(
                    "FYERS history API returned non-ok status for {Symbol}: s={Status}, c={Code}.",
                    fyersSymbol, body?.S, body?.C);
                return Array.Empty<OhlcvRecord>();
            }

            return body.Data.Candles
                .Select(candle => MapCandle(symbol, candle))
                .Where(r => r is not null)
                .OfType<OhlcvRecord>()
                .ToList();
        }, cancellationToken);

        return result;
    }

    public async Task<QuoteRecord?> GetLatestQuoteAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var fyersSymbol = FyersSymbolFormatter.ToFyersFormat(symbol);

        _logger.LogDebug("Fetching latest quote for {Symbol} (FYERS: {FyersSymbol}).", symbol, fyersSymbol);

        return await _throttle.ExecuteAsync(async ct =>
        {
            var token = await GetTokenAsync(ct);
            if (token is null)
            {
                _logger.LogError("Cannot fetch quote: FYERS access token is unavailable.");
                return null;
            }

            var requestBody = new
            {
                symbols = fyersSymbol,
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{FyersApiEndpoints.BaseUrl}{FyersApiEndpoints.Quotes}")
            {
                Content = JsonContent.Create(requestBody),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<FyersQuoteResponse>(JsonOptions, ct);

            if (body?.S != "ok" || body.D is null || body.D.Count == 0)
            {
                _logger.LogWarning(
                    "FYERS quote API returned no data for {Symbol}: s={Status}, c={Code}.",
                    fyersSymbol, body?.S, body?.C);
                return null;
            }

            var quoteData = body.D[0];
            return MapQuote(symbol, quoteData);
        }, cancellationToken);
    }

    public async IAsyncEnumerable<QuoteRecord> SubscribeToLivePrices(
        IEnumerable<string> symbols,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var symbolList = symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (symbolList.Count == 0)
            yield break;

        _logger.LogInformation(
            "Starting REST polling subscription for {Count} symbols via FYERS quotes API.",
            symbolList.Count);

        // REST polling: iterate through symbols and poll GetLatestQuoteAsync.
        // The throttle enforces per-second limits, preventing excessive API calls.
        // Polling interval is driven by the throttle's natural rate limiting
        // rather than a fixed timer, which ensures we never exceed provider limits.
        var semaphore = new SemaphoreSlim(1, 1);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Stagger polling across the symbol list to spread load.
            foreach (var symbol in symbolList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                QuoteRecord? quote = null;
                try
                {
                    quote = await GetLatestQuoteAsync(symbol, cancellationToken);
                }
                catch (RateLimitExceededException)
                {
                    _logger.LogWarning(
                        "Rate limit reached during live price polling for {Symbol}. " +
                        "Will resume on next cycle.",
                        symbol);
                    // Budget exhausted — pause until next cycle.
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Error polling live price for {Symbol}. Continuing with next symbol.",
                        symbol);
                }

                if (quote is not null)
                {
                    yield return quote;
                }
            }

            // Wait for one full poll interval before the next cycle.
            // The throttle ensures we don't exceed per-second limits during the cycle.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }
    }

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        try
        {
            return await _accessTokenProvider();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve FYERS access token from token provider.");
            return null;
        }
    }

    private static OhlcvRecord? MapCandle(string symbol, FyersCandle candle)
    {
        // FYERS candle: [timestamp_epoch, open, high, low, close, volume]
        if (candle.Count < 6)
            return null;

        var timestamp = candle[0];
        var open = candle[1];
        var high = candle[2];
        var low = candle[3];
        var close_ = candle[4];
        var volume = candle[5];

        var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

        return new OhlcvRecord(
            Symbol: symbol,
            Date: DateOnly.FromDateTime(date),
            Open: open,
            High: high,
            Low: low,
            Close: close_,
            Volume: (long)volume);
    }

    private static QuoteRecord? MapQuote(string symbol, FyersQuoteDetail quote)
    {
        // Check for the nested `v` field structure.
        if (quote.V is null)
            return null;

        var lastPrice = quote.V.Lp;
        var change = quote.V.Ch;
        var changePercent = quote.V.Chp;
        var timestamp = quote.V.Timestamp;
        var isDelayed = quote.V.IsDelayed > 0;

        var utcTimestamp = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;

        return new QuoteRecord(
            Symbol: symbol,
            LastPrice: lastPrice,
            Change: change,
            ChangePercent: changePercent,
            TimestampUtc: utcTimestamp,
            IsDelayed: isDelayed);
    }

    // ── FYERS API response DTOs ──────────────────────────────────────────────

    private sealed record FyersHistoryResponse
    {
        [JsonPropertyName("s")]
        public string? S { get; init; }

        [JsonPropertyName("c")]
        public int C { get; init; }

        [JsonPropertyName("data")]
        public FyersHistoryData? Data { get; init; }
    }

    private sealed record FyersHistoryData
    {
        [JsonPropertyName("candles")]
        public List<FyersCandle>? Candles { get; init; }
    }

    /// <summary>FYERS candle: [timestamp, open, high, low, close, volume].</summary>
    private sealed class FyersCandle : List<long>;

    private sealed record FyersQuoteResponse
    {
        [JsonPropertyName("s")]
        public string? S { get; init; }

        [JsonPropertyName("c")]
        public int C { get; init; }

        [JsonPropertyName("d")]
        public List<FyersQuoteDetail>? D { get; init; }
    }

    private sealed record FyersQuoteDetail
    {
        [JsonPropertyName("n")]
        public string? N { get; init; }

        [JsonPropertyName("v")]
        public FyersQuoteData? V { get; init; }
    }

    private sealed record FyersQuoteData
    {
        [JsonPropertyName("lp")]
        public decimal Lp { get; init; }

        [JsonPropertyName("ch")]
        public decimal Ch { get; init; }

        [JsonPropertyName("chp")]
        public decimal Chp { get; init; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; init; }

        [JsonPropertyName("is_delayed")]
        public int IsDelayed { get; init; }
    }
}
