namespace SignalStack.Api.Backtesting;

/// <summary>
/// Repository for persisting and retrieving backtest results.
/// REQ-BTSTORE-001: all backtest output persisted to SQL Server.
/// REQ-BTSTORE-002: tables use BT_ prefix.
/// </summary>
public interface IBacktestRepository
{
    /// <summary>Persists a completed backtest run header.</summary>
    Task SaveRunHeaderAsync(BacktestRunResult result, CancellationToken ct = default);

    /// <summary>Persists per-trade records for a backtest run.</summary>
    Task SaveTradesAsync(Guid runId, List<BacktestTrade> trades, CancellationToken ct = default);

    /// <summary>Persists position lifecycle events for a backtest run.</summary>
    Task SavePositionEventsAsync(Guid runId, List<BacktestPositionEvent> events, CancellationToken ct = default);

    /// <summary>Persists portfolio-level performance and equity curve.</summary>
    Task SavePortfolioPerformanceAsync(BacktestRunResult result, CancellationToken ct = default);

    /// <summary>Returns a backtest run result by run ID, or null if not found.</summary>
    Task<BacktestRunResult?> GetRunAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Returns run headers for the most recent runs, ordered by timestamp descending.</summary>
    Task<List<BacktestRunHeader>> ListRunsAsync(int limit = 20, int offset = 0, CancellationToken ct = default);
}

/// <summary>Lightweight run header without full trade data.</summary>
public sealed record BacktestRunHeader
{
    public required Guid RunId { get; init; }
    public required string SignalType { get; init; }
    public required string Timeframe { get; init; }
    public required DateOnly DateRangeStart { get; init; }
    public required DateOnly DateRangeEnd { get; init; }
    public decimal StartingEquity { get; init; }
    public decimal EndingEquity { get; init; }
    public int TotalTrades { get; init; }
    public decimal ExpectancyInR { get; init; }
    public DateTime RunTimestamp { get; init; }
}
