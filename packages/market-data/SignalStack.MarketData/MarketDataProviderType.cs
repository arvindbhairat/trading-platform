namespace SignalStack.MarketData;

/// <summary>
/// Identifies which concrete market data provider implementation is active.
///
/// The active provider is determined at startup from <c>market_data.provider.active</c>
/// in <c>sys_config</c> per REQ-MKTPROV-005. Switching providers requires a
/// configuration change and rolling restart; it never requires a code deployment.
/// </summary>
public enum MarketDataProviderType
{
    /// <summary>FYERS — initial provider used during testing/evaluation.</summary>
    Fyers = 0,

    /// <summary>TrueData — supported third-party commercial provider.</summary>
    TrueData = 1,

    /// <summary>Global Data Feeds — supported third-party commercial provider.</summary>
    GlobalDataFeeds = 2,
}
