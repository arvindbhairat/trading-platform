namespace SignalStack.MarketData.Throttling;

/// <summary>
/// Centralised throttle for external market-data provider API calls per the
/// External API Integration Standards in engineering-standards.md.
///
/// Enforces per-second, per-minute, and per-day limits sourced from
/// <c>sys_config</c>. Treats HTTP 429 responses as transient errors with
/// configurable exponential backoff. Exposes current-day consumption as an
/// observable metric for operators to monitor headroom against the daily limit.
///
/// One instance per provider (FYERS, TrueData, GDF) with provider-specific
/// rate limits bound from the <c>integrations.&lt;provider&gt;.rate_limit.*</c>
/// config keys.
/// </summary>
public interface IMarketDataThrottle
{
    /// <summary>
    /// Execute an API call through the throttle.
    ///
    /// Before invoking <paramref name="operation"/>, the throttle acquires
    /// permits from the per-second and per-minute rate limiters and checks
    /// the daily budget. If the operation returns an HTTP 429 response,
    /// the throttle applies exponential backoff and retries (up to a
    /// reasonable number of attempts). On success, the daily budget counter
    /// is incremented.
    ///
    /// Throws <see cref="RateLimitExceededException"/> if the daily budget
    /// is exhausted before any attempt is made, or if retries are exhausted
    /// due to persistent 429 responses.
    /// </summary>
    /// <param name="operation">The async operation to execute. Typically an HTTP call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <typeparam name="TResult">The operation's return type.</typeparam>
    /// <returns>The operation's result.</returns>
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}
