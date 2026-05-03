using Microsoft.Extensions.Logging;

namespace SignalStack.MarketData.Throttling;

/// <summary>
/// Thread-safe daily API call budget tracker that resets at UTC midnight.
///
/// Uses a <see cref="Timer"/> to reset the counter at the next UTC midnight
/// boundary. The timer fires once per day and does not tick again until
/// the next calendar day. Accurate under sustained load; minor clock skew
/// (±1 s) is acceptable for budget monitoring purposes.
/// </summary>
internal sealed class DailyBudgetTracker : IDisposable
{
    private long _currentCount;
    private long _peakCount;
    private readonly int _dailyLimit;
    private readonly object _lock = new();
    private readonly ILogger<DailyBudgetTracker> _logger;
    private Timer? _resetTimer;
    private bool _disposed;

    public DailyBudgetTracker(int dailyLimit, ILogger<DailyBudgetTracker> logger)
    {
        _dailyLimit = dailyLimit > 0 ? dailyLimit : throw new ArgumentOutOfRangeException(nameof(dailyLimit));
        _logger = logger;
        ScheduleNextReset();
    }

    /// <summary>Current API call count since last UTC midnight reset.</summary>
    public long CurrentCount => Interlocked.Read(ref _currentCount);

    /// <summary>Highest concurrent count observed in the current window (informational).</summary>
    public long PeakCount => Interlocked.Read(ref _peakCount);

    /// <summary>Calls remaining before the daily limit is reached.</summary>
    public long RemainingBudget => _dailyLimit - Interlocked.Read(ref _currentCount);

    /// <summary>Usage as a percentage of the daily limit (0–100).</summary>
    public double UsagePercent => (double)Interlocked.Read(ref _currentCount) / _dailyLimit * 100.0;

    /// <summary>
    /// Attempt to increment the daily counter.
    /// Returns <c>true</c> if the counter was incremented (budget available),
    /// or <c>false</c> if the daily limit has been reached.
    /// </summary>
    public bool TryIncrement()
    {
        long current = Interlocked.Increment(ref _currentCount);
        UpdatePeak(current);
        return current <= _dailyLimit;
    }

    /// <summary>
    /// Decrement the counter (used when a request fails and should not count
    /// against the budget even though it consumed a rate-limit permit).
    /// </summary>
    public void Decrement()
    {
        Interlocked.Decrement(ref _currentCount);
    }

    /// <summary>Reset the counter to zero. Called automatically at UTC midnight.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            var previousCount = Interlocked.Exchange(ref _currentCount, 0);
            Interlocked.Exchange(ref _peakCount, 0);
            _logger.LogInformation(
                "Daily API budget reset. Previous window consumed {Count} calls (limit: {Limit}, peak: {Peak}).",
                previousCount, _dailyLimit, _peakCount);
        }
    }

    private void UpdatePeak(long current)
    {
        long peak;
        do
        {
            peak = Interlocked.Read(ref _peakCount);
            if (current <= peak) break;
        }
        while (Interlocked.CompareExchange(ref _peakCount, current, peak) != peak);
    }

    private void ScheduleNextReset()
    {
        var now = DateTime.UtcNow;
        var nextMidnight = now.Date.AddDays(1);
        var delay = nextMidnight - now;

        lock (_lock)
        {
            _resetTimer?.Dispose();
            _resetTimer = new Timer(
                _ =>
                {
                    Reset();
                    ScheduleNextReset();
                },
                null,
                delay,
                Timeout.InfiniteTimeSpan);
        }

        _logger.LogDebug(
            "DailyBudgetTracker: next reset scheduled at {ResetTime} UTC ({DelayMinutes:F1} min from now).",
            nextMidnight, delay.TotalMinutes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _resetTimer?.Dispose();
            _resetTimer = null;
        }
    }
}
