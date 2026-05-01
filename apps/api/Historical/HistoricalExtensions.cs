using SignalStack.Api.Admin;

namespace SignalStack.Api.Historical;

/// <summary>
/// DI registration extensions for the Historical/Charting subsystem.
///
/// Registers:
/// - <see cref="ISymbolTableMapping"/> / <see cref="SymbolTableMappingService"/> — single shared mapping (REQ-HIST-011)
/// - <see cref="IOhlcvRepository"/> / <see cref="SqlOhlcvRepository"/> — SQL Server OHLCV queries
/// - <see cref="ITimeframeService"/> / <see cref="TimeframeService"/> — shared timeframe resolution (REQ-TIMEFRAME-004)
///
/// REQ-HIST-003/004: table names constructed from <c>sql_table_name_suffix</c> via the mapping service.
/// </summary>
public static class HistoricalExtensions
{
    public static IServiceCollection AddHistoricalServices(
        this IServiceCollection services,
        string? sqlConnectionString)
    {
        // Shared timeframe resolution — single source of truth for all timeframe definitions.
        // REQ-TIMEFRAME-004: charting, signals, backtesting, and EODSR all use this service.
        // REQ-CALENDAR-003: calendar-aware via ITradingCalendarRepository.
        services.AddSingleton<ITimeframeService, TimeframeService>();

        // Symbol-table mapping — single shared service for all SQL Server table name resolution.
        // REQ-HIST-011: Every consumer must use this service.
        services.AddSingleton<ISymbolTableMapping, SymbolTableMappingService>();

        // OHLCV repository — queries per-symbol D_, W_, M_ tables from SQL Server.
        if (!string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            services.AddSingleton<IOhlcvRepository>(sp =>
            {
                var tableMapping = sp.GetRequiredService<ISymbolTableMapping>();
                var logger = sp.GetRequiredService<ILogger<SqlOhlcvRepository>>();
                return new SqlOhlcvRepository(sqlConnectionString, tableMapping, logger);
            });
        }
        else
        {
            // Fallback: register a no-op implementation for environments without SQL Server.
            services.AddSingleton<IOhlcvRepository>(_ => new NoopOhlcvRepository());
        }

        return services;
    }

    /// <summary>
    /// Initializes the <see cref="ISymbolTableMapping"/> cache on application startup.
    /// Should be called from the application entry point after building the app.
    /// </summary>
    public static async Task InitializeHistoricalServicesAsync(this WebApplication app)
    {
        var tableMapping = app.Services.GetRequiredService<ISymbolTableMapping>();
        if (tableMapping is SymbolTableMappingService svc)
        {
            await svc.InitializeAsync();
        }
    }
}

/// <summary>
/// Fallback OHLCV repository used when SQL Server connection string is not configured.
/// Always returns empty results.
/// </summary>
internal sealed class NoopOhlcvRepository : IOhlcvRepository
{
    public Task<List<OhlcvRecord>> GetDailyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult(new List<OhlcvRecord>());

    public Task<List<OhlcvRecord>> GetWeeklyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult(new List<OhlcvRecord>());

    public Task<List<OhlcvRecord>> GetMonthlyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
        => Task.FromResult(new List<OhlcvRecord>());

    public Task<List<OhlcvRecord>> GetRollingAsync(string symbol, int windowSize, DateOnly to, CancellationToken ct = default)
        => Task.FromResult(new List<OhlcvRecord>());

    public Task<DateOnly?> GetLastCandleDateAsync(string symbol, CancellationToken ct = default)
        => Task.FromResult<DateOnly?>(null);
}
