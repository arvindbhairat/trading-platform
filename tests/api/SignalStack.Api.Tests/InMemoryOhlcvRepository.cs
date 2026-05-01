using SignalStack.Api.Historical;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory implementation of <see cref="IOhlcvRepository"/> for testing.
/// Provides seeded OHLCV data that can be queried by symbol and timeframe.
/// </summary>
public sealed class InMemoryOhlcvRepository : IOhlcvRepository
{
    private readonly Dictionary<string, List<OhlcvRecord>> _dailyData = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<OhlcvRecord>> _weeklyData = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<OhlcvRecord>> _monthlyData = new(StringComparer.OrdinalIgnoreCase);

    public void SeedDaily(string symbol, params OhlcvRecord[] records)
    {
        _dailyData[symbol] = [.. records.OrderBy(r => r.Date)];
    }

    public void SeedWeekly(string symbol, params OhlcvRecord[] records)
    {
        _weeklyData[symbol] = [.. records.OrderBy(r => r.Date)];
    }

    public void SeedMonthly(string symbol, params OhlcvRecord[] records)
    {
        _monthlyData[symbol] = [.. records.OrderBy(r => r.Date)];
    }

    public Task<List<OhlcvRecord>> GetDailyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!_dailyData.TryGetValue(symbol, out var all)) return Task.FromResult(new List<OhlcvRecord>());
        var filtered = all.Where(r => r.Date >= from && r.Date <= to).ToList();
        return Task.FromResult(filtered);
    }

    public Task<List<OhlcvRecord>> GetWeeklyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!_weeklyData.TryGetValue(symbol, out var all)) return Task.FromResult(new List<OhlcvRecord>());
        var filtered = all.Where(r => r.Date >= from && r.Date <= to).ToList();
        return Task.FromResult(filtered);
    }

    public Task<List<OhlcvRecord>> GetMonthlyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!_monthlyData.TryGetValue(symbol, out var all)) return Task.FromResult(new List<OhlcvRecord>());
        var filtered = all.Where(r => r.Date >= from && r.Date <= to).ToList();
        return Task.FromResult(filtered);
    }

    public Task<List<OhlcvRecord>> GetRollingAsync(string symbol, int windowSize, DateOnly to, CancellationToken ct = default)
    {
        if (!_dailyData.TryGetValue(symbol, out var all)) return Task.FromResult(new List<OhlcvRecord>());

        var window = all.Where(r => r.Date <= to)
            .OrderByDescending(r => r.Date)
            .Take(windowSize)
            .OrderBy(r => r.Date)
            .ToList();

        if (window.Count == 0) return Task.FromResult(new List<OhlcvRecord>());

        // Build a single rolling candle
        var first = window[0];
        var last = window[^1];
        return Task.FromResult(new List<OhlcvRecord>
        {
            new OhlcvRecord(
                Date: first.Date,
                Open: first.Open,
                High: window.Max(r => r.High),
                Low: window.Min(r => r.Low),
                Close: last.Close,
                Volume: window.Sum(r => r.Volume)
            )
        });
    }

    public Task<DateOnly?> GetLastCandleDateAsync(string symbol, CancellationToken ct = default)
    {
        if (!_dailyData.TryGetValue(symbol, out var all) || all.Count == 0)
            return Task.FromResult<DateOnly?>(null);

        return Task.FromResult<DateOnly?>(all.Max(r => r.Date));
    }
}
