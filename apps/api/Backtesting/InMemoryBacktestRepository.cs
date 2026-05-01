using System.Collections.Concurrent;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// In-memory fallback repository for backtest results when SQL Server is
/// not configured. Used in development/testing environments.
/// </summary>
public sealed class InMemoryBacktestRepository : IBacktestRepository
{
    private readonly ConcurrentDictionary<Guid, BacktestRunResult> _runs = new();
    private readonly ConcurrentBag<BacktestRunHeader> _headers = new();
    private readonly ILogger<InMemoryBacktestRepository> _logger;

    public InMemoryBacktestRepository(ILogger<InMemoryBacktestRepository> logger)
    {
        _logger = logger;
    }

    public Task SaveRunHeaderAsync(BacktestRunResult result, CancellationToken ct = default)
    {
        _runs[result.RunId] = result;
        _headers.Add(new BacktestRunHeader
        {
            RunId = result.RunId,
            SignalType = result.SignalType,
            Timeframe = result.Timeframe,
            DateRangeStart = result.DateRangeStart,
            DateRangeEnd = result.DateRangeEnd,
            StartingEquity = result.StartingEquity,
            EndingEquity = result.EndingEquity,
            TotalTrades = result.TotalTrades,
            ExpectancyInR = result.ExpectancyInR,
            RunTimestamp = result.RunTimestamp,
        });

        _logger.LogInformation("InMemory: saved backtest run {RunId}.", result.RunId);
        return Task.CompletedTask;
    }

    public Task SaveTradesAsync(Guid runId, List<BacktestTrade> trades, CancellationToken ct = default)
    {
        // Trades are already stored in the result object
        return Task.CompletedTask;
    }

    public Task SavePositionEventsAsync(Guid runId, List<BacktestPositionEvent> events, CancellationToken ct = default)
    {
        // Events are already stored in the result object
        return Task.CompletedTask;
    }

    public Task SavePortfolioPerformanceAsync(BacktestRunResult result, CancellationToken ct = default)
    {
        // Performance is already stored in the result object
        return Task.CompletedTask;
    }

    public Task<BacktestRunResult?> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        _runs.TryGetValue(runId, out var result);
        return Task.FromResult(result);
    }

    public Task<List<BacktestRunHeader>> ListRunsAsync(int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        var headers = _headers
            .OrderByDescending(h => h.RunTimestamp)
            .Skip(offset)
            .Take(limit)
            .ToList();

        return Task.FromResult(headers);
    }
}
