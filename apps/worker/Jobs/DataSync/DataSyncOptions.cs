using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Jobs.DataSync;

/// <summary>
/// Configuration options for the DataSync (DS) job.
///
/// REQ-MARKET-003: post-market DataSync refreshes SQL Server historical data
/// before any end-of-day signal scan runs.
/// REQ-MARKET-009: fetches daily candles from the current trading session
/// minus <see cref="RecoverySessionCount"/> sessions through the current session.
/// </summary>
public sealed class DataSyncOptions
{
    public const string SectionName = "DataSync";

    /// <summary>
    /// How often the DS worker polls for pending DS jobs or checks whether
    /// a run is needed. Default: 120 seconds.
    /// </summary>
    [Range(10, 3600)]
    public int PollIntervalSeconds { get; init; } = 120;

    /// <summary>
    /// Number of trading sessions to look back when determining which sessions
    /// need syncing. Default: 10 sessions per REQ-MARKET-009.
    /// </summary>
    [Range(1, 30)]
    public int RecoverySessionCount { get; init; } = 10;

    /// <summary>
    /// Number of symbols to process per session before yielding progress.
    /// Larger batches improve throughput at the cost of larger transactions.
    /// Default: 50 symbols.
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Maximum number of daily records to insert in a single bulk operation.
    /// Default: 1000 rows.
    /// </summary>
    [Range(100, 10000)]
    public int BulkInsertBatchSize { get; init; } = 1000;
}
