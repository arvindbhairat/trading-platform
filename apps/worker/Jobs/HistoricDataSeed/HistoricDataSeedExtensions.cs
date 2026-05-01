using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SignalStack.Api.Historical;
using SignalStack.MarketData;

namespace SignalStack.Worker.Jobs.HistoricDataSeed;

/// <summary>
/// DI registration extensions for the HistoricDataSeed (HDS) job.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddHistoricDataSeed(builder.Configuration);
/// </code>
///
/// Registers:
/// - <see cref="HistoricDataSeedOptions"/> bound to the <c>HistoricDataSeed</c> config section
/// - <see cref="HistoricDataSeedService"/> as a singleton
/// - <see cref="HistoricDataSeedWorker"/> as a hosted BackgroundService
/// </summary>
public static class HistoricDataSeedExtensions
{
    /// <summary>
    /// Register HDS services for per-symbol historical data seeding.
    /// REQ-HIST-009/009a: idempotent, resumable, per-symbol table materialisation.
    /// </summary>
    public static IServiceCollection AddHistoricDataSeed(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Bind configuration options ─────────────────────────────
        services
            .AddOptions<HistoricDataSeedOptions>()
            .BindConfiguration(HistoricDataSeedOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Step 2: Register the core HDS service ──────────────────────────
        // Uses scoped resolution to get the SQL connection string at runtime
        // from configuration (available after bootstrap configuration has loaded).
        services.AddSingleton<HistoricDataSeedService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var sqlConnectionString = config.GetConnectionString("SqlServer");
            var tableMapping = sp.GetRequiredService<ISymbolTableMapping>();
            var marketDataProvider = sp.GetRequiredService<IMarketDataProvider>();
            var ohlcvRepo = sp.GetRequiredService<IOhlcvRepository>();
            var logger = sp.GetRequiredService<ILogger<HistoricDataSeedService>>();

            return new HistoricDataSeedService(
                sqlConnectionString ?? string.Empty,
                tableMapping,
                marketDataProvider,
                ohlcvRepo,
                logger);
        });

        // ── Step 3: Register the BackgroundService worker ──────────────────
        services.AddHostedService<HistoricDataSeedWorker>();

        return services;
    }
}
