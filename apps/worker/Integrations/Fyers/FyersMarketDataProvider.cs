using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
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
/// Authentication: FYERS v3 uses <c>AppID:{access_token}</c> format via the
/// Authorization header (not Bearer). The AppID is injected from configuration
/// and the token is provided by <c>accessTokenProvider</c>.
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
    private readonly string _appId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly JsonSerializerOptions LoggingJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public FyersMarketDataProvider(
        HttpClient httpClient,
        IMarketDataThrottle throttle,
        Func<Task<string?>> accessTokenProvider,
        ILogger<FyersMarketDataProvider> logger,
        string appId)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _appId = appId ?? throw new ArgumentNullException(nameof(appId));
    }

    public string ProviderName => "FYERS";

    /// <summary>Maximum days per FYERS historical data request (daily resolution).</summary>
    private const int MaxDaysPerRequest = 365;

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

        // FYERS API v3 has a 366-day limit per request for "D" resolution.
        // Paginate the date range into chunks of MaxDaysPerRequest to stay safe.
        var allResults = new List<OhlcvRecord>();
        var currentFrom = fromDate;

        while (currentFrom <= toDate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkTo = currentFrom.AddDays(MaxDaysPerRequest - 1);
            if (chunkTo > toDate)
                chunkTo = toDate;

            var chunk = await FetchHistoricalChunkAsync(symbol, fyersSymbol, currentFrom, chunkTo, cancellationToken);
            if (chunk is not null)
                allResults.AddRange(chunk);

            currentFrom = chunkTo.AddDays(1);
        }

        return allResults;
    }

    /// <summary>
    /// Fetches one chunk of historical OHLCV data within the per-request day limit.
    /// Uses <c>date_format = "1"</c> with human-readable <c>yyyy-MM-dd</c> date strings
    /// for easy log readability.
    /// </summary>
    private async Task<IReadOnlyList<OhlcvRecord>> FetchHistoricalChunkAsync(
        string symbol,
        string fyersSymbol,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        return await _throttle.ExecuteAsync<IReadOnlyList<OhlcvRecord>>(async ct =>
        {
            var authHeader = await BuildAuthHeaderAsync(ct);
            if (authHeader is null)
            {
                _logger.LogError("Cannot fetch historical data: FYERS access token is unavailable.");
                return Array.Empty<OhlcvRecord>();
            }

            var endpoint = FyersApiEndpoints.History;

            // FYERS API v3 uses GET with query parameters for history, not POST with body.
            var queryString = $"?symbol={Uri.EscapeDataString(fyersSymbol)}" +
                $"&resolution=D&date_format=1" +
                $"&range_from={fromDate:yyyy-MM-dd}" +
                $"&range_to={toDate:yyyy-MM-dd}" +
                $"&cont_flag=1";

            var url = $"{FyersApiEndpoints.MarketDataBaseUrl}{endpoint}{queryString}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = authHeader;

            var requestBodyForLogging = $"(GET query string: {queryString})";

            return (await SendAndParseAsync<FyersHistoryResponse, IReadOnlyList<OhlcvRecord>>(
                request, requestBodyForLogging, endpoint, fyersSymbol,
                (body, _) => body?.S == "ok" && body.Data?.Candles is not null,
                (body, sym) => body!.Data!.Candles!
                    .Select(candle => MapCandle(sym!, candle))
                    .Where(r => r is not null)
                    .OfType<OhlcvRecord>()
                    .ToList(),
                symbol,
                ct))!;
        }, cancellationToken);
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
            var authHeader = await BuildAuthHeaderAsync(ct);
            if (authHeader is null)
            {
                _logger.LogError("Cannot fetch quote: FYERS access token is unavailable.");
                return null;
            }

            var requestBody = new
            {
                symbols = fyersSymbol,
            };

            var requestBodyJson = JsonSerializer.Serialize(requestBody, LoggingJsonOptions);
            var endpoint = FyersApiEndpoints.Quotes;

            var request = new HttpRequestMessage(HttpMethod.Post, $"{FyersApiEndpoints.BaseUrl}{endpoint}")
            {
                Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = authHeader;

            var result = await SendAndParseAsync<FyersQuoteResponse, QuoteRecord?>(
                request, requestBodyJson, endpoint, fyersSymbol,
                (body, _) => body?.S == "ok" && body.D is { Count: > 0 },
                (body, sym) => MapQuote(sym!, body!.D![0]),
                symbol,
                ct);

            return result;
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

        while (!cancellationToken.IsCancellationRequested)
        {
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

    // ── Private helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds the FYERS v3 Authorization header in <c>AppID:{access_token}</c> format.
    /// Returns null if the token could not be obtained.
    /// </summary>
    private async Task<AuthenticationHeaderValue?> BuildAuthHeaderAsync(CancellationToken ct)
    {
        try
        {
            var token = await _accessTokenProvider();
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogError("FYERS access token is null or empty.");
                return null;
            }

            _logger.LogDebug("Building FYERS auth header with AppID: {AppId}", _appId);
            return new AuthenticationHeaderValue(_appId, $":{token}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve FYERS access token from token provider.");
            return null;
        }
    }

    /// <summary>
    /// Sends a request to the FYERS API, logs the full request/response on failure,
    /// and parses the response using the provided guards and mapper.
    ///
    /// Type parameters:
    ///  <typeparam name="TResponse">The FYERS response DTO type.</typeparam>
    ///  <typeparam name="TResult">The result type to map to.</typeparam>
    /// </summary>
    private async Task<TResult?> SendAndParseAsync<TResponse, TResult>(
        HttpRequestMessage request,
        string requestBodyJson,
        string endpoint,
        string fyersSymbol,
        Func<TResponse?, string?, bool> isValid,
        Func<TResponse?, string?, TResult?> map,
        string? symbol,
        CancellationToken ct)
        where TResponse : class
    {
        var responseBody = (string?)null;

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "FYERS API error: {Method} {Endpoint} for {Symbol} returned {StatusCode}. " +
                    "Request body: {RequestBody}. Response body: {ResponseBody}",
                    request.Method, endpoint, fyersSymbol,
                    (int)response.StatusCode,
                    requestBodyJson,
                    responseBody);
                response.EnsureSuccessStatusCode();
            }

            var body = JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions);

            if (!isValid(body, symbol))
            {
                _logger.LogWarning(
                    "FYERS {Endpoint} returned non-ok status for {Symbol}: " +
                    "Request body: {RequestBody}. Response body: {ResponseBody}",
                    endpoint, fyersSymbol, requestBodyJson, responseBody);
                return default;
            }

            _logger.LogDebug(
                "FYERS {Endpoint} for {Symbol} succeeded. Request: {RequestBody}",
                endpoint, fyersSymbol, requestBodyJson);

            return map(body, symbol);
        }
        catch (HttpRequestException)
        {
            // responseBody is already logged above in the error path.
            // Re-throw so the throttle layer handles it as a non-retryable error.
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "FYERS {Endpoint} for {Symbol} returned unparseable JSON. " +
                "Request body: {RequestBody}. Response body: {ResponseBody}",
                endpoint, fyersSymbol, requestBodyJson, responseBody ?? "(none)");
            throw;
        }
    }

    // ── Candle / quote mapping ──────────────────────────────────────────────

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
