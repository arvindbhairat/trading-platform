namespace SignalStack.Worker.Integrations.Fyers;

/// <summary>
/// FYERS API v3 endpoint constants.
///
/// Base URL: <c>https://api.fyers.in/api/v3</c>
/// Authentication: Bearer token in <c>Authorization</c> header.
/// </summary>
internal static class FyersApiEndpoints
{
    /// <summary>Base URL for FYERS API v3.</summary>
    public const string BaseUrl = "https://api.fyers.in/api/v3";

    /// <summary>Historical OHLCV data endpoint. POST.</summary>
    public const string History = "/data/history";

    /// <summary>Quote endpoint. POST.</summary>
    public const string Quotes = "/quotes";

    /// <summary>Market status endpoint. GET.</summary>
    public const string MarketStatus = "/marketStatus";
}
