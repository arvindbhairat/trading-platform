using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SignalStack.MarketData;

/// <summary>
/// DI service registration extensions for the Market Data Provider abstraction.
///
/// Call <see cref="AddMarketDataProviderResolution"/> in the application's
/// startup configuration to register the provider resolution service.
///
/// The actual <see cref="IMarketDataProvider"/> adapter registration is the
/// responsibility of the concrete provider package (e.g., FYERS adapter registered
/// in its own extension method). This ensures provider-specific dependencies
/// are never pulled into the shared contract assembly.
/// </summary>
public static class MarketDataServiceExtensions
{
    /// <summary>
    /// Register the <see cref="MarketDataProviderResolution"/> service and the
    /// marker service that enables provider-resolution from config.
    /// </summary>
    public static IServiceCollection AddMarketDataProviderResolution(
        this IServiceCollection services)
    {
        services.TryAddSingleton<MarketDataProviderResolution>();
        return services;
    }
}
