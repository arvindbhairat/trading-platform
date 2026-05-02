using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Jobs.EodSignalRunner;

/// <summary>
/// Configuration options for the EOD Signal Runner (EODSR) job.
///
/// REQ-STRAT-013: reads SQL Server data and evaluates user-subscribed Signal types.
/// REQ-STRAT-027: atomic-run semantics with db-retry up to <see cref="DbRetryMaxSeconds"/>.
/// </summary>
public sealed class EodSignalRunnerOptions
{
    public const string SectionName = "EodSignalRunner";

    /// <summary>
    /// How often the EODSR worker polls for pending work.
    /// Default: 300 seconds (5 minutes).
    /// </summary>
    [Range(10, 3600)]
    public int PollIntervalSeconds { get; init; } = 300;

    /// <summary>
    /// Number of symbols to evaluate before yielding backpressure.
    /// Default: 50 symbols.
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Maximum number of daily candles to fetch per symbol for signal evaluation.
    /// Default: 500 (ample for most indicator windows).
    /// </summary>
    [Range(30, 5000)]
    public int MaxCandlesPerSymbol { get; init; } = 500;
}
