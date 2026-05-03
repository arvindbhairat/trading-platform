using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SignalStack.MarketData.Throttling;

/// <summary>
/// DI registration extensions for <see cref="IMarketDataThrottle"/>.
///
/// Usage:
/// <code>
/// // Register FYERS throttle (in worker Program.cs):
/// services.AddMarketDataThrottle(
///     "fyers",
///     configuration.GetSection("integrations:fyers:rate_limit"));
/// </code>
///
/// Each provider gets its own throttle instance with provider-specific rate
/// limits bound from the <c>integrations.&lt;provider&gt;.rate_limit.*</c> config keys.
/// </summary>
public static class MarketDataThrottleExtensions
{
    /// <summary>
    /// Register a named <see cref="IMarketDataThrottle"/> for the given provider.
    ///
    /// The throttle is registered as a keyed singleton by <paramref name="providerName"/>.
    /// Use <see cref="GetRequiredThrottle"/> to resolve it by name in adapter code.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="providerName">Provider name, e.g., "fyers", "truedata", "gdf".</param>
    /// <param name="configurationSection">
    /// The config section containing rate_limit keys (per_second, per_minute, per_day, etc.).
    /// Typically bound from <c>integrations.&lt;provider&gt;.rate_limit</c>.
    /// </param>
    public static void AddMarketDataThrottle(
        this IServiceCollection services,
        string providerName,
        IConfigurationSection configurationSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(configurationSection);

        // Bind options eagerly so no IOptions dependency is needed in the package.
        var options = new MarketDataThrottleOptions();
        configurationSection.Bind(options);

        // Register metrics as singleton per provider name.
        services.AddKeyedSingleton<MarketDataThrottleMetrics>(
            providerName,
            (_, _) => new MarketDataThrottleMetrics(providerName));

        // Register throttle as keyed singleton per provider name.
        services.AddKeyedSingleton<IMarketDataThrottle>(
            providerName,
            (sp, _) =>
            {
                var metrics = sp.GetRequiredKeyedService<MarketDataThrottleMetrics>(providerName);
                var logger = sp.GetRequiredService<ILogger<MarketDataThrottle>>();
                return new MarketDataThrottle(options, metrics, logger, providerName);
            });
    }

    /// <summary>
    /// Resolve a previously registered <see cref="IMarketDataThrottle"/> by provider name.
    /// </summary>
    public static IMarketDataThrottle GetRequiredThrottle(
        this IServiceProvider services,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        return services.GetRequiredKeyedService<IMarketDataThrottle>(providerName);
    }
}
