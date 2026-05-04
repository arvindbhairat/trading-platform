using System.Collections.Concurrent;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// Concurrency semaphore for backtest and optimisation run execution.
/// REQ-SLO-007: capped at 5 platform-wide and 1 per user.
/// U-9: optimisation runs share the same cap (additive with backtest runs).
///
/// Uses a semaphore for the platform-wide cap and per-user tracking for the
/// per-user cap. Queue position is tracked for each waiter so the portal can
/// show the user their place in line.
/// </summary>
public sealed class BacktestConcurrencySemaphore : IAsyncDisposable
{
    private readonly SemaphoreSlim _platformSemaphore;
    private readonly ConcurrentDictionary<string, int> _userRunCounts = new();
    private readonly ConcurrentDictionary<string, QueueEntry> _queue = new();
    private int _globalQueueCounter;

    /// <summary>Maximum concurrent runs platform-wide.</summary>
    public int PlatformCap { get; }
    /// <summary>Maximum concurrent runs per user.</summary>
    public int PerUserCap { get; }

    public BacktestConcurrencySemaphore(int platformCap = 5, int perUserCap = 1)
    {
        PlatformCap = platformCap;
        PerUserCap = perUserCap;
        _platformSemaphore = new SemaphoreSlim(platformCap, platformCap);
    }

    /// <summary>
    /// Attempts to acquire a slot. Returns a handle that must be disposed when done,
    /// or null if the per-user cap is exceeded.
    /// </summary>
    public async Task<ConcurrencySlot?> AcquireAsync(string userId, CancellationToken ct = default)
    {
        // Check per-user cap
        var currentUserCount = _userRunCounts.AddOrUpdate(userId, 1, (_, count) => count + 1);
        if (currentUserCount > PerUserCap)
        {
            _userRunCounts.AddOrUpdate(userId, 0, (_, count) => count - 1);
            return null; // Per-user cap exceeded
        }

        var queuePos = Interlocked.Increment(ref _globalQueueCounter);
        var entry = new QueueEntry(userId, queuePos);
        _queue[userId] = entry;

        await _platformSemaphore.WaitAsync(ct);

        // We got through — remove from queue tracking
        _queue.TryRemove(userId, out _);

        return new ConcurrencySlot(this, userId, queuePos);
    }

    /// <summary>Returns the current queue position for a user, or 0 if not queued.</summary>
    public int GetQueuePosition(string userId)
    {
        if (_queue.TryGetValue(userId, out var entry))
        {
            // Calculate actual position relative to semaphore count
            var semaphoreCount = _platformSemaphore.CurrentCount;
            var effectivelyQueued = Math.Max(0, entry.QueueNumber - (_globalQueueCounter - PlatformCap + semaphoreCount));
            return effectivelyQueued > 0 ? effectivelyQueued : 0;
        }
        return 0;
    }

    /// <summary>Number of currently active runs.</summary>
    public int ActiveCount => PlatformCap - _platformSemaphore.CurrentCount;

    /// <summary>Current queue depth (waiting count).</summary>
    public int QueueDepth => _queue.Count;

    internal void Release(string userId)
    {
        _userRunCounts.AddOrUpdate(userId, 0, (_, count) => Math.Max(0, count - 1));
        _platformSemaphore.Release();
    }

    public ValueTask DisposeAsync()
    {
        _platformSemaphore.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record QueueEntry(string UserId, int QueueNumber);
}

/// <summary>Disposable handle for an acquired concurrency slot.</summary>
public sealed class ConcurrencySlot : IDisposable
{
    private readonly BacktestConcurrencySemaphore _semaphore;
    private readonly string _userId;
    private int _disposed;

    public string UserId { get; }
    public int QueuePosition { get; }

    internal ConcurrencySlot(BacktestConcurrencySemaphore semaphore, string userId, int queuePosition)
    {
        _semaphore = semaphore;
        _userId = userId;
        UserId = userId;
        QueuePosition = queuePosition;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _semaphore.Release(_userId);
        }
    }
}
