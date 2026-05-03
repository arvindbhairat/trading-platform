namespace SignalStack.MarketData.Throttling;

/// <summary>
/// Thrown when the throttle's daily budget is exhausted or retries are
/// exhausted due to persistent HTTP 429 responses.
/// </summary>
public sealed class RateLimitExceededException : Exception
{
    public RateLimitExceededException(string message) : base(message)
    {
    }

    public RateLimitExceededException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
