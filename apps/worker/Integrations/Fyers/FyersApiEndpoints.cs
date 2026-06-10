namespace SignalStack.Worker.Integrations.Fyers;

/// <summary>
/// FYERS API v3 endpoint constants.
///
/// Authentication: <c>AppID:{access_token}</c> via Authorization header.
///
/// FYERS v3 uses separate base URLs for market data and user/transaction APIs:
///   - Market Data: <c>https://api-t1.fyers.in/data/...</c>
///   - User/Transaction: <c>https://api-t1.fyers.in/api/v3/...</c>
/// </summary>
internal static class FyersApiEndpoints
{
    /// <summary>Base URL for user and transaction APIs (/api/v3/*).</summary>
    public const string BaseUrl = "https://api-t1.fyers.in/api/v3";

    /// <summary>Base URL for market data APIs (/data/*).</summary>
    public const string MarketDataBaseUrl = "https://api-t1.fyers.in/data";

    /// <summary>Historical OHLCV data endpoint. GET with query parameters.</summary>
    public const string History = "/history";

    /// <summary>Quote endpoint. GET with query parameters.</summary>
    public const string Quotes = "/quotes";

    /// <summary>Market status endpoint. GET.</summary>
    public const string MarketStatus = "/marketStatus";
}
