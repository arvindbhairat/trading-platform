using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SignalStack.Historical;
using SignalStack.Storage.Historical;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// DI registration extensions for Historical/Charting services used by the Worker.
/// Registers timeframe, symbol-table mapping, and OHLCV repository services.
/// </summary>
public static class HistoricalWorkerExtensions
{
    public static IServiceCollection AddHistoricalServices(
        this IServiceCollection services,
        string? sqlConnectionString)
    {
        services.AddSingleton<ITimeframeService, TimeframeService>();
        services.AddSingleton<ISymbolTableMapping, SymbolTableMappingService>();

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
            services.AddSingleton<IOhlcvRepository>(_ => new NoopOhlcvRepository());
        }

        return services;
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
