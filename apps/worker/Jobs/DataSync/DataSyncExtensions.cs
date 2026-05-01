using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SignalStack.Api.Admin;
using SignalStack.Api.Historical;
using SignalStack.Api.Universe;
using SignalStack.MarketData;

namespace SignalStack.Worker.Jobs.DataSync;

/// <summary>
/// DI registration extensions for the DataSync (DS) job.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddDataSync(builder.Configuration);
/// </code>
///
/// Registers:
/// - <see cref="DataSyncOptions"/> bound to the <c>DataSync</c> config section
/// - <see cref="DataSyncService"/> as a singleton
/// - <see cref="DataSyncWorker"/> as a hosted BackgroundService
///
/// REQ-MARKET-003:  post-market DataSync refreshes SQL Server historical data.
/// REQ-MARKET-005:  fails on provider/auth failure with structured Error log.
/// REQ-MARKET-005a: re-checks admin token at every session boundary.
/// REQ-MARKET-006:  skips symbols with unavailable daily data.
/// REQ-MARKET-007:  writes explicit success marker for the trading session.
/// REQ-MARKET-009:  10-session auto-recovery for missed sync days.
/// REQ-MARKET-013:  validates each OHLCV candle before writing.
/// </summary>
public static class DataSyncExtensions
{
    /// <summary>
    /// Register DataSync services for daily incremental OHLCV sync.
    /// </summary>
    public static IServiceCollection AddDataSync(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Bind configuration options ─────────────────────────────
        services
            .AddOptions<DataSyncOptions>()
            .BindConfiguration(DataSyncOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Step 2: Register the core DataSync service ──────────────────────
        services.AddSingleton<DataSyncService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var sqlConnectionString = config.GetConnectionString("SqlServer");
            var tableMapping = sp.GetRequiredService<ISymbolTableMapping>();
            var marketDataProvider = sp.GetRequiredService<IMarketDataProvider>();
            var ohlcvRepo = sp.GetRequiredService<IOhlcvRepository>();
            var symbolMasterRepo = sp.GetRequiredService<ISymbolMasterRepository>();
            var calendarRepo = sp.GetRequiredService<ITradingCalendarRepository>();
            var adminTokenProvider = sp.GetRequiredService<Func<Task<string?>>>();
            var logger = sp.GetRequiredService<ILogger<DataSyncService>>();

            return new DataSyncService(
                sqlConnectionString ?? string.Empty,
                tableMapping,
                marketDataProvider,
                ohlcvRepo,
                symbolMasterRepo,
                calendarRepo,
                adminTokenProvider,
                logger);
        });

        // ── Step 3: Register the BackgroundService worker ──────────────────
        services.AddHostedService<DataSyncWorker>();

        return services;
    }
}
