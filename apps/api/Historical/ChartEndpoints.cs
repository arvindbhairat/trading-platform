using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace SignalStack.Api.Historical;

/// <summary>
/// Chart data endpoints that return OHLCV from SQL Server via <see cref="ISymbolTableMapping"/>.
///
/// REQ-HIST-011: All table names resolved through the shared mapping service.
/// REQ-TIMEFRAME-005: W_ and M_ tables are pre-computed; D_ used for daily and rolling.
/// REQ-HIST-004: Tables referenced using <c>sql_table_name_suffix</c>, not symbol text.
/// </summary>
public static class ChartEndpoints
{
    private static readonly HashSet<string> ValidTimeframes = ["daily", "weekly", "monthly", "d", "w", "m"];

    public static void MapChartEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/chart");

        // GET /api/v1/chart/{symbol}?timeframe=daily&from=2024-01-01&to=2024-12-31
        group.MapGet("/{symbol}", GetChartDataAsync)
            .WithName("GetChartData")
            .WithOpenApi();

        // GET /api/v1/chart/{symbol}/last  — most recent candle date
        group.MapGet("/{symbol}/last", GetLastCandleDateAsync)
            .WithName("GetLastCandleDate")
            .WithOpenApi();

        // GET /api/v1/chart/{symbol}/rolling?window=5&to=2024-12-31
        group.MapGet("/{symbol}/rolling", GetRollingCandleAsync)
            .WithName("GetRollingCandle")
            .WithOpenApi();
    }

    /// <summary>
    /// Returns OHLCV chart data for the specified symbol and timeframe.
    /// </summary>
    /// <param name="symbol">NSE trading symbol.</param>
    /// <param name="timeframe">One of: daily, weekly, monthly (or d, w, m). Default: daily.</param>
    /// <param name="from">Start date (ISO 8601). Default: 1 year ago.</param>
    /// <param name="to">End date (ISO 8601). Default: today.</param>
    /// <param name="repository">Injected.</param>
    /// <param name="tableMapping">Injected.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<Results<Ok<List<OhlcvRecord>>, NotFound<string>>> GetChartDataAsync(
        string symbol,
        [FromQuery] string? timeframe,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromServices] IOhlcvRepository repository,
        [FromServices] ISymbolTableMapping tableMapping,
        CancellationToken ct)
    {
        // Validate symbol exists in the mapping.
        var suffix = await tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null)
        {
            return TypedResults.NotFound($"Symbol '{symbol}' not found in symbol master.");
        }

        var tf = (timeframe ?? "daily").ToLowerInvariant();

        if (!ValidTimeframes.Contains(tf))
        {
            return TypedResults.NotFound(
                $"Invalid timeframe '{timeframe}'. Valid values: daily, weekly, monthly (or d, w, m).");
        }

        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);

        List<OhlcvRecord> results = tf switch
        {
            "daily" or "d" => await repository.GetDailyAsync(symbol, fromDate, toDate, ct),
            "weekly" or "w" => await repository.GetWeeklyAsync(symbol, fromDate, toDate, ct),
            "monthly" or "m" => await repository.GetMonthlyAsync(symbol, fromDate, toDate, ct),
            _ => [] // Unreachable due to validation above.
        };

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
    /// </summary>
    /// <param name="symbol">NSE trading symbol.</param>
    /// <param name="window">Number of sessions (3, 5, or 7). Default: 5.</param>
    /// <param name="to">End date (ISO 8601). Default: today.</param>
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
