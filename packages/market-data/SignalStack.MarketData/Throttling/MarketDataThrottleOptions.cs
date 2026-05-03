using System.ComponentModel.DataAnnotations;

namespace SignalStack.MarketData.Throttling;

/// <summary>
/// Configuration options for the <see cref="MarketDataThrottle"/>.
///
/// Bound from <c>integrations.&lt;provider&gt;.rate_limit.*</c> keys in
/// <c>sys_config</c> (e.g., <c>integrations.fyers.rate_limit.per_second</c>).
/// </summary>
public sealed class MarketDataThrottleOptions
{
    public const string SectionName = "rate_limit";

    /// <summary>Maximum API requests per second. Default: 10.</summary>
    [Range(1, 10_000)]
    public int PerSecond { get; set; } = 10;

    /// <summary>Maximum API requests per minute. Default: 200.</summary>
    [Range(1, 600_000)]
    public int PerMinute { get; set; } = 200;

    /// <summary>Maximum API requests per calendar day (UTC). Default: 100000.</summary>
    [Range(1, 10_000_000)]
    public int PerDay { get; set; } = 100_000;

    /// <summary>Initial backoff delay in milliseconds for 429 responses. Default: 1000.</summary>
    [Range(100, 30_000)]
    public int BackoffInitialMs { get; set; } = 1000;

    /// <summary>Maximum backoff delay in milliseconds for 429 responses. Default: 60000.</summary>
    [Range(1_000, 300_000)]
    public int BackoffMaxMs { get; set; } = 60_000;

    /// <summary>Exponential backoff multiplier per retry. Default: 2.0.</summary>
    [Range(1.0, 10.0)]
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum number of retries on 429 response before throwing
    /// <see cref="RateLimitExceededException"/>. Default: 5.
    /// </summary>
    [Range(0, 20)]
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Queue wait time in milliseconds above which a warning is logged.
    /// Default: 5000 (5 seconds).
    /// </summary>
    [Range(100, 120_000)]
    public int QueueWarnThresholdMs { get; set; } = 5_000;
}
