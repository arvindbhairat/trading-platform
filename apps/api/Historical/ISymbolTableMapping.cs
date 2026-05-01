namespace SignalStack.Api.Historical;

/// <summary>
/// Single shared mapping service for SQL Server table name resolution.
///
/// REQ-HIST-011: Every platform component that requires an SQL Server table name
/// for a symbol must obtain it through this service. Components must not construct
/// table names by applying the REQ-HIST-006 cleansing algorithm directly.
///
/// The mapping is loaded in memory from <c>symbol_master</c> at application startup
/// and refreshed on successful completion of each Universe Sync. This rule binds
/// the charting layer, the backtesting engine, the EOD Signal Runner, and any other
/// consumer of SQL Server historical data; divergence between consumers is treated
/// as a defect.
/// </summary>
public interface ISymbolTableMapping
{
    /// <summary>
    /// Returns the three SQL Server table names (D_, W_, M_) for the given symbol.
    /// </summary>
    /// <param name="symbol">The NSE trading symbol (e.g. "RELIANCE").</param>
    /// <returns>
    /// A tuple of (dailyTable, weeklyTable, monthlyTable) or null if the symbol
    /// is not found in the symbol master.
    /// </returns>
    Task<(string Daily, string Weekly, string Monthly)?> GetTableNamesAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Returns the SQL table name suffix for the given symbol.
    /// </summary>
    Task<string?> GetSuffixAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Returns all known suffixes keyed by symbol. Used for bulk operations
    /// (DataSync, EODSR, backtesting).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetAllSuffixesAsync(CancellationToken ct = default);

    /// <summary>
    /// Refreshes the in-memory mapping from the symbol master.
    /// Called automatically on startup and after each successful Universe Sync.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the timeframe table name prefix for the given timeframe string.
    /// "daily" -> "D_", "weekly" -> "W_", "monthly" -> "M_".
    /// Throws ArgumentException for unknown timeframes.
    /// </summary>
    static string TimeframePrefix(string timeframe) => timeframe?.ToLowerInvariant() switch
    {
        "daily" or "d" => "D_",
        "weekly" or "w" => "W_",
        "monthly" or "m" => "M_",
        _ => throw new ArgumentException(
            $"Unknown timeframe '{timeframe}'. Valid values: daily/d, weekly/w, monthly/m.", nameof(timeframe))
    };
}
