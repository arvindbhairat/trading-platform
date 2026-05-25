namespace SignalStack.Storage.Historical;

/// <summary>
/// Repository for querying OHLCV data from SQL Server per-symbol tables.
///
/// REQ-HIST-001/002: three tables per symbol (D_, W_, M_) with Date/Open/High/Low/Close/Volume.
/// REQ-HIST-011: all table names must be resolved through <see cref="ISymbolTableMapping"/>.
/// REQ-TIMEFRAME-006: rolling window candles are computed on demand.
/// </summary>
public interface IOhlcvRepository
{
    /// <summary>
    /// Returns daily OHLCV records for a given symbol and date range.
    /// </summary>
    Task<List<OhlcvRecord>> GetDailyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Returns weekly OHLCV records for a given symbol and date range.
    /// Pre-computed from the W_ table per REQ-TIMEFRAME-005.
    /// </summary>
    Task<List<OhlcvRecord>> GetWeeklyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Returns monthly OHLCV records for a given symbol and date range.
    /// Pre-computed from the M_ table per REQ-TIMEFRAME-005.
    /// </summary>
    Task<List<OhlcvRecord>> GetMonthlyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Returns rolling N-session candles computed on demand from the D_ table
    /// per REQ-TIMEFRAME-006 and REQ-HIST-010a.
    /// </summary>
    /// <param name="symbol">NSE trading symbol.</param>
    /// <param name="windowSize">Number of sessions (3, 5, or 7).</param>
    /// <param name="to">End date (inclusive).</param>
    Task<List<OhlcvRecord>> GetRollingAsync(string symbol, int windowSize, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent Date available in the symbol's D_ table.
    /// Returns null if the table has no rows or does not exist.
    /// </summary>
    Task<DateOnly?> GetLastCandleDateAsync(string symbol, CancellationToken ct = default);
}

/// <summary>
/// A single OHLCV data point returned by <see cref="IOhlcvRepository"/>.
/// </summary>
public sealed record OhlcvRecord(
    DateOnly Date,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume
);
