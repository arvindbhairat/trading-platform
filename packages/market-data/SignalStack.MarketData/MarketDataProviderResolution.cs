using Microsoft.Extensions.Logging;

namespace SignalStack.MarketData;

/// <summary>
/// Resolves the active <see cref="MarketDataProviderType"/> from runtime
/// configuration per REQ-MKTPROV-005.
///
/// The active provider is determined by the <c>market_data.provider.active</c>
/// key in <c>sys_config</c>. Supported values:
/// <c>"fyers"</c>, <c>"truedata"</c>, <c>"gdf"</c> (Global Data Feeds).
///
/// Resolution is case-insensitive. Unrecognised values log a warning and
/// default to <see cref="MarketDataProviderType.Fyers"/> for safe fallback
/// during the FYERS-era evaluation phase.
/// </summary>
public class MarketDataProviderResolution
{
    private readonly ILogger<MarketDataProviderResolution> _logger;

    public MarketDataProviderResolution(ILogger<MarketDataProviderResolution> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolve a <see cref="MarketDataProviderType"/> from the raw string value
    /// of <c>market_data.provider.active</c>.
    /// </summary>
    public MarketDataProviderType Resolve(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            _logger.LogWarning(
                "market_data.provider.active is null or empty; defaulting to {DefaultProvider}",
                MarketDataProviderType.Fyers);
            return MarketDataProviderType.Fyers;
        }

        var normalized = providerName.Trim().ToLowerInvariant();

        var result = normalized switch
        {
            "fyers" => MarketDataProviderType.Fyers,
            "truedata" => MarketDataProviderType.TrueData,
            "gdf" => MarketDataProviderType.GlobalDataFeeds,
            _ => (MarketDataProviderType?)null,
        };

        if (result is null)
        {
            _logger.LogWarning(
                "Unrecognised market_data.provider.active value '{Provider}'; " +
                "defaulting to {DefaultProvider}. Supported values: fyers, truedata, gdf.",
                providerName, MarketDataProviderType.Fyers);
            return MarketDataProviderType.Fyers;
        }

        _logger.LogInformation(
            "Active market data provider resolved to {Provider}",
            result.Value);
        return result.Value;
    }
}
