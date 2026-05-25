using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SignalStack.Historical;
using SignalStack.Storage.Historical;

namespace SignalStack.Api.Historical;

/// <summary>
/// Chart data endpoints that return OHLCV from SQL Server via <see cref="ISymbolTableMapping"/>.
///
/// REQ-HIST-011: All table names resolved through the shared mapping service.
/// REQ-TIMEFRAME-004: Timeframe resolution uses <see cref="ITimeframeService"/>.
/// REQ-TIMEFRAME-005: W_ and M_ tables are pre-computed; D_ used for daily and rolling.
/// REQ-TIMEFRAME-006: Rolling candles computed on demand from D_ tables.
/// </summary>
public static class ChartEndpoints
{
    public static void MapChartEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/chart");

        // GET /api/v1/chart/{symbol}?timeframe=daily&from=2024-01-01&to=2024-12-31
        group.MapGet("/{symbol}", GetChartDataAsync);

        // GET /api/v1/chart/{symbol}/last  — most recent candle date
        group.MapGet("/{symbol}/last", GetLastCandleDateAsync);

        // GET /api/v1/chart/{symbol}/rolling?window=5&to=2024-12-31
        // Kept for backward compatibility; callers should prefer ?timeframe=rolling5.
        group.MapGet("/{symbol}/rolling", GetRollingCandleAsync);
    }

    /// <summary>
    /// Returns OHLCV chart data for the specified symbol and timeframe.
    /// Supports daily, weekly, monthly, rolling3, rolling5, and rolling7.
    /// </summary>
    /// <param name="symbol">NSE trading symbol.</param>
    /// <param name="timeframe">
    /// One of: daily (default), weekly, monthly, rolling3, rolling5, rolling7
    /// (or short aliases: d, w, m, 3, 5, 7).
    /// </param>
    /// <param name="from">Start date (ISO 8601). Default: 1 year ago. Ignored for rolling timeframes.</param>
    /// <param name="to">End date (ISO 8601). Default: today.</param>
    /// <param name="repository">Injected.</param>
    /// <param name="tableMapping">Injected.</param>
    /// <param name="timeframeService">Injected.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<Results<Ok<List<OhlcvRecord>>, NotFound<string>>> GetChartDataAsync(
        string symbol,
        [FromQuery] string? timeframe,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IOhlcvRepository repository,
        [FromServices] ISymbolTableMapping tableMapping,
        [FromServices] ITimeframeService timeframeService,
        CancellationToken ct)
    {
        // Validate symbol exists in the mapping.
        var suffix = await tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null)
        {
            return TypedResults.NotFound($"Symbol '{symbol}' not found in symbol master.");
        }

        var tf = timeframeService.Resolve(timeframe ?? "daily");
        if (tf is null)
        {
            var valid = string.Join(", ", timeframeService.GetAll().Select(t => t.Name));
            return TypedResults.NotFound(
                $"Invalid timeframe '{timeframe}'. Valid values: {valid}.");
        }

        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        List<OhlcvRecord> results;

        if (tf.IsRolling && tf.WindowSize.HasValue)
        {
            // REQ-TIMEFRAME-006: rolling candles computed on demand from D_ table.
            results = await repository.GetRollingAsync(symbol, tf.WindowSize.Value, toDate, ct);
        }
        else
        {
            var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
            results = tf.Name switch
            {
                "daily"   => await repository.GetDailyAsync(symbol, fromDate, toDate, ct),
                "weekly"  => await repository.GetWeeklyAsync(symbol, fromDate, toDate, ct),
                "monthly" => await repository.GetMonthlyAsync(symbol, fromDate, toDate, ct),
                _ => [] // Unreachable due to validation above.
            };
        }

        return TypedResults.Ok(results);
    }

    /// <summary>
    /// Returns the most recent candle date available for the symbol.
    /// </summary>
    public static async Task<Results<Ok<LastCandleResponse>, NotFound<string>>> GetLastCandleDateAsync(
        string symbol,
        [FromServices] IOhlcvRepository repository,
        [FromServices] ISymbolTableMapping tableMapping,
        CancellationToken ct)
    {
        var suffix = await tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null)
        {
            return TypedResults.NotFound($"Symbol '{symbol}' not found in symbol master.");
        }

        var lastDate = await repository.GetLastCandleDateAsync(symbol, ct);

        return TypedResults.Ok(new LastCandleResponse(
            Symbol: symbol,
            LastCandleDate: lastDate
        ));
    }

    /// <summary>
    /// Returns a rolling N-session candle computed on demand from the D_ table.
    /// Legacy endpoint; prefer using the main chart endpoint with ?timeframe=rollingN.
    /// </summary>
    public static async Task<Results<Ok<List<OhlcvRecord>>, NotFound<string>, BadRequest<string>>> GetRollingCandleAsync(
        string symbol,
        [FromQuery] int? window,
        [FromQuery] DateOnly? to,
        [FromServices] IOhlcvRepository repository,
        [FromServices] ISymbolTableMapping tableMapping,
        CancellationToken ct)
    {
        var suffix = await tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null)
        {
            return TypedResults.NotFound($"Symbol '{symbol}' not found in symbol master.");
        }

        var windowSize = window ?? 5;
        if (windowSize is < 1 or > 365)
        {
            return TypedResults.BadRequest("Window size must be between 1 and 365.");
        }

        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var results = await repository.GetRollingAsync(symbol, windowSize, toDate, ct);

        return TypedResults.Ok(results);
    }
}

/// <summary>Response model for the last-candle-date endpoint.</summary>
public sealed record LastCandleResponse(string Symbol, DateOnly? LastCandleDate);
