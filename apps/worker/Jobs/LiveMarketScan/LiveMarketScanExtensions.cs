using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Worker.Jobs.LiveMarketScan;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// DI registration extensions for the Live Market Data Scan (LMDS) job.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddLiveMarketScan(builder.Configuration);
/// </code>
///
/// REQ-STOP-006:   continuous intraday price + level monitoring during market hours.
/// REQ-STOP-006b:  per-position level evaluation (stop, add, reduce, trailing stop).
/// REQ-STOP-006c:  quote-delay indicator processing.
/// REQ-SLO-004:    cycle must complete within 70 % of configured poll interval.
/// REQ-SLO-011:    capacity warning at sys_config threshold.
/// </summary>
public static class LiveMarketScanExtensions
{
    /// <summary>
    /// Register Live Market Data Scan (LMDS) services.
    /// </summary>
    public static IServiceCollection AddLiveMarketScan(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Bind configuration options ─────────────────────────────
        services
            .AddOptions<LiveMarketScanOptions>()
            .BindConfiguration(LiveMarketScanOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Step 2: Register the core scan service ─────────────────────────
        services.AddSingleton<LiveMarketScanService>();

        // ── Step 3: Register the BackgroundService worker ──────────────────
        services.AddHostedService<LiveMarketScanWorker>();

        return services;
    }
}
