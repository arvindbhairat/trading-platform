using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;

namespace SignalStack.MarketData.Throttling;

/// <summary>
/// Centralised throttle implementation enforcing per-second, per-minute, and
/// per-day API call limits with configurable exponential backoff on 429
/// responses. See <see cref="IMarketDataThrottle"/> for the contract.
///
/// Rate limiters (token-bucket for per-second and per-minute, sliding-window
/// counter for per-day) are created from <see cref="MarketDataThrottleOptions"/>.
/// Metrics are emitted through <see cref="MarketDataThrottleMetrics"/>.
/// </summary>
public sealed class MarketDataThrottle : IMarketDataThrottle, IDisposable
{
    private readonly TokenBucketRateLimiter _perSecondLimiter;
    private readonly TokenBucketRateLimiter _perMinuteLimiter;
    private readonly DailyBudgetTracker _dailyBudget;
    private readonly MarketDataThrottleOptions _options;
    private readonly MarketDataThrottleMetrics _metrics;
    private readonly ILogger<MarketDataThrottle> _logger;
    private readonly string _providerName;
    private bool _disposed;

    public MarketDataThrottle(
        MarketDataThrottleOptions options,
        MarketDataThrottleMetrics metrics,
        ILogger<MarketDataThrottle> logger,
        string? providerName = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerName = providerName ?? "unknown";

        _perSecondLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = _options.PerSecond,
            TokensPerPeriod = _options.PerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });

        _perMinuteLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = _options.PerMinute,
            TokensPerPeriod = _options.PerMinute,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });

        _dailyBudget = new DailyBudgetTracker(_options.PerDay,
    new LoggerFactory().CreateLogger<DailyBudgetTracker>());

        // Wire budget gauge providers so ObservableGauge values update on read.
        _metrics.SetBudgetProviders(
            remainingBudget: () => _dailyBudget.RemainingBudget,
            usagePercent: () => _dailyBudget.UsagePercent);
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // ── Step 1: Acquire per-second permit ────────────────────────────
        var sw = ValueStopwatch.StartNew();
        using var perSecondLease = await _perSecondLimiter.AcquireAsync(
            permitCount: 1, cancellationToken);

        if (!perSecondLease.IsAcquired)
        {
            _logger.LogWarning(
                "Per-second rate limit exceeded for provider '{Provider}'. Rejecting request.",
                _providerName);
            _metrics.ThrottledResponsesTotal.Add(1);
            throw new RateLimitExceededException(
                $"Per-second rate limit ({_options.PerSecond}/s) exceeded for provider '{_providerName}'.");
        }
        var queueWaitMs = sw.Elapsed.TotalMilliseconds;
        _metrics.ThrottleQueueWaitMs.Record(queueWaitMs);

        if (queueWaitMs > _options.QueueWarnThresholdMs)
        {
            _logger.LogWarning(
                "Throttle queue wait for provider '{Provider}' was {WaitMs:F0} ms " +
                "(threshold: {ThresholdMs} ms).",
                _providerName, queueWaitMs, _options.QueueWarnThresholdMs);
        }

        // ── Step 2: Acquire per-minute permit ────────────────────────────
        using var perMinuteLease = await _perMinuteLimiter.AcquireAsync(
            permitCount: 1, cancellationToken);

        if (!perMinuteLease.IsAcquired)
        {
            _logger.LogWarning(
                "Per-minute rate limit exceeded for provider '{Provider}'. Rejecting request.",
                _providerName);
            _metrics.ThrottledResponsesTotal.Add(1);
            throw new RateLimitExceededException(
                $"Per-minute rate limit ({_options.PerMinute}/min) exceeded for provider '{_providerName}'.");
        }

        // ── Step 3: Check daily budget ───────────────────────────────────
        if (_dailyBudget.RemainingBudget <= 0)
        {
            _logger.LogError(
                "Daily API budget exhausted for provider '{Provider}'. " +
                "Limit: {Limit}, consumed: {Consumed}.",
                _providerName, _options.PerDay, _dailyBudget.CurrentCount);
            _metrics.BudgetExhaustionTotal.Add(1);
            throw new RateLimitExceededException(
                $"Daily API budget ({_options.PerDay}) exhausted for provider '{_providerName}'.");
        }

        // ── Step 4: Execute with 429 retry loop ──────────────────────────
        var attempt = 0;
        var delayMs = _options.BackoffInitialMs;

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await operation(cancellationToken);
                _dailyBudget.TryIncrement();
                _metrics.ApiCallsTotal.Add(1,
                    new KeyValuePair<string, object?>("provider", _providerName),
                    new KeyValuePair<string, object?>("status", "success"));

                _logger.LogTrace(
                    "API call via throttle for provider '{Provider}' succeeded (attempt {Attempt}). " +
                    "Daily budget: {Consumed}/{Limit}.",
                    _providerName, attempt, _dailyBudget.CurrentCount, _options.PerDay);

                return result;
            }
            catch (RateLimitExceededException)
            {
                // Re-thrown if retries exhausted after persistent 429 responses.
                if (attempt > _options.MaxRetries)
                {
                    _metrics.BudgetExhaustionTotal.Add(1);
                    _metrics.ApiCallsTotal.Add(1,
                        new KeyValuePair<string, object?>("provider", _providerName),
                        new KeyValuePair<string, object?>("status", "throttled"));
                    throw;
                }

                await BackoffAsync(attempt, delayMs, cancellationToken);
                delayMs = (int)Math.Min(delayMs * _options.BackoffMultiplier, _options.BackoffMaxMs);
            }
            catch (HttpRequestException ex) when (attempt <= _options.MaxRetries)
            {
                // Retry on HTTP 429 (rate limited) and transient network errors.
                // Do NOT retry non-429 client errors (4xx) — those are application errors.
                if (ex.StatusCode.HasValue && (int)ex.StatusCode.Value != 429)
                {
                    _logger.LogError(ex,
                        "Non-retryable HTTP error for provider '{Provider}' (attempt {Attempt}): {StatusCode}",
                        _providerName, attempt, ex.StatusCode);
                    throw;
                }

                _logger.LogWarning(ex,
                    "Transient HTTP failure for provider '{Provider}' (attempt {Attempt}/{MaxRetries}): {Message}",
                    _providerName, attempt, _options.MaxRetries, ex.Message);

                _metrics.ThrottledResponsesTotal.Add(1,
                    new KeyValuePair<string, object?>("provider", _providerName));

                await BackoffAsync(attempt, delayMs, cancellationToken);
                delayMs = (int)Math.Min(delayMs * _options.BackoffMultiplier, _options.BackoffMaxMs);
            }
        }
    }

    private async Task BackoffAsync(int attempt, int delayMs, CancellationToken cancellationToken)
    {
        var jitter = Random.Shared.Next(0, (int)(delayMs * 0.1)); // 0–10% jitter
        var totalDelay = delayMs + jitter;

        _logger.LogInformation(
            "Rate-limit backoff for provider '{Provider}': attempt {Attempt}, " +
            "delay {DelayMs} ms (jitter: {Jitter} ms).",
            _providerName, attempt, totalDelay, jitter);

        _metrics.ThrottledResponsesTotal.Add(1,
            new KeyValuePair<string, object?>("provider", _providerName));

        try
        {
            await Task.Delay(totalDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Backoff cancelled for provider '{Provider}' on attempt {Attempt}.",
                _providerName, attempt);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _perSecondLimiter.Dispose();
        _perMinuteLimiter.Dispose();
        _dailyBudget.Dispose();
        _metrics.Dispose();
    }
}
