using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Jobs.HistoricDataSeed;

/// <summary>
/// Configuration options for the HistoricDataSeed (HDS) job.
///
/// REQ-HIST-009: per-symbol table materialisation for historical OHLCV data.
/// REQ-HIST-009a: idempotent and resumable from last successful date.
/// </summary>
public sealed class HistoricDataSeedOptions
{
    public const string SectionName = "HistoricDataSeed";

    /// <summary>
    /// How often the HDS worker polls the <c>job_runs</c> collection for
    /// pending seed jobs. Default: 30 seconds.
    /// </summary>
    [Range(1, 3600)]
    public int PollIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Safety-floor lookback for the initial backfill cursor when the
    /// symbol's D_ table is empty. The actual backfill iterates backwards
    /// until the API returns no candles (reaching inception), so this value
    /// is only a conservative starting offset, not the final depth.
    ///
    /// Default: 10 years. NSE was founded in 1992 (~33 years from 2025),
    /// but the algorithm will automatically iterate further back past this
    /// value as needed.
    /// </summary>
    [Range(1, 50)]
    public int HistoricalYearsBack { get; init; } = 10;

    /// <summary>
    /// Batch size for inserting daily OHLCV records in a single round-trip.
    /// Larger batches improve throughput at the cost of larger transactions.
    /// Default: 500 rows.
    /// </summary>
    [Range(1, 5000)]
    public int BatchSize { get; init; } = 500;
}
