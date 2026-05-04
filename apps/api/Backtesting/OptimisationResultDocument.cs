using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// MongoDB document for a completed RME profile optimisation run.
/// Stored per symbol + subscription combination.
/// REQ-RME-021: results are stored per symbol and surfaced as suggested
/// default profile when the user next configures an entry for that symbol.
/// </summary>
public sealed class OptimisationResultDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Platform user ID who triggered the optimisation.</summary>
    [BsonElement("user_id")]
    public required string UserId { get; init; }

    /// <summary>Signal Subscription ID this optimisation was run for.</summary>
    [BsonElement("subscription_id")]
    public required ObjectId SubscriptionId { get; init; }

    /// <summary>Symbol this optimisation evaluated.</summary>
    [BsonElement("symbol")]
    public required string Symbol { get; init; }

    [BsonElement("triggered_at")]
    public DateTime TriggeredAt { get; init; }

    [BsonElement("completed_at")]
    public DateTime? CompletedAt { get; set; }

    /// <summary>"running", "completed", "failed"</summary>
    [BsonElement("status")]
    public string Status { get; set; } = "running";

    /// <summary>Date range used for the optimisation backtests.</summary>
    [BsonElement("date_range_start")]
    public required DateOnly DateRangeStart { get; init; }

    [BsonElement("date_range_end")]
    public required DateOnly DateRangeEnd { get; init; }

    /// <summary>The subscription version that was in effect when triggered.</summary>
    [BsonElement("signal_subscription_version_id")]
    public string SignalSubscriptionVersionId { get; init; } = "";

    /// <summary>
    /// Signal code versions that contributed to this run, keyed by dependency name.
    /// Used for staleness check (c).
    /// </summary>
    [BsonElement("signal_code_versions")]
    public Dictionary<string, string> SignalCodeVersions { get; init; } = [];

    /// <summary>Per-profile configuration results, ranked by final score descending.</summary>
    [BsonElement("results")]
    public required List<ProfileConfigResult> Results { get; init; }

    /// <summary>Developer-defined grid that was used for this run.</summary>
    [BsonElement("grid_snapshot")]
    public string GridSnapshotJson { get; init; } = "";

    /// <summary>Comma-separated list of all symbols evaluated (for Robustness Factor cross-symbol consistency).</summary>
    [BsonElement("evaluated_symbols")]
    public List<string> EvaluatedSymbols { get; init; } = [];
}

/// <summary>Result for a single profile configuration within an optimisation run.</summary>
public sealed record ProfileConfigResult
{
    /// <summary>Serialised RmeProfile JSON for this configuration.</summary>
    [BsonElement("profile_json")]
    public required string ProfileJson { get; init; }

    /// <summary>Human-readable label for this profile.</summary>
    [BsonElement("profile_label")]
    public required string ProfileLabel { get; init; }

    // ── Backtest metrics ─────────────────────────────────────
    [BsonElement("total_trades")]
    public int TotalTrades { get; init; }

    [BsonElement("winning_trades")]
    public int WinningTrades { get; init; }

    [BsonElement("losing_trades")]
    public int LosingTrades { get; init; }

    [BsonElement("win_rate_pct")]
    public decimal WinRatePct { get; init; }

    [BsonElement("average_r")]
    public decimal AverageR { get; init; }

    [BsonElement("expectancy_in_r")]
    public decimal ExpectancyInR { get; init; }

    [BsonElement("profit_factor")]
    public decimal ProfitFactor { get; init; }

    [BsonElement("max_drawdown_pct")]
    public decimal MaxDrawdownPct { get; init; }

    [BsonElement("sharpe_ratio")]
    public decimal SharpeRatio { get; init; }

    [BsonElement("total_return_annualised_pct")]
    public decimal TotalReturnAnnualisedPct { get; init; }

    // ── Composite Score (REQ-RME-020a) ───────────────────────
    [BsonElement("composite_score")]
    public decimal CompositeScore { get; init; }

    /// <summary>Individual component scores that make up the Composite Score.</summary>
    [BsonElement("component_scores")]
    public Dictionary<string, decimal> ComponentScores { get; init; } = [];

    // ── Robustness Factor (REQ-RME-020b) ─────────────────────
    [BsonElement("robustness_factor")]
    public decimal RobustnessFactor { get; init; }

    /// <summary>Individual sub-scores of the Robustness Factor.</summary>
    [BsonElement("robustness_sub_scores")]
    public Dictionary<string, decimal> RobustnessSubScores { get; init; } = [];

    /// <summary>Final score = CompositeScore × RobustnessFactor.</summary>
    [BsonElement("final_score")]
    public decimal FinalScore { get; init; }

    /// <summary>True when trade count is below min_trades_for_valid_result threshold.</summary>
    [BsonElement("is_low_confidence")]
    public bool IsLowConfidence { get; init; }
}
