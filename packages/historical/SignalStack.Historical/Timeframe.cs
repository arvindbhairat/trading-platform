namespace SignalStack.Historical;

/// <summary>
/// First-class timeframe definition used across charting, signals, backtesting,
/// and the EOD Signal Runner.
///
/// REQ-TIMEFRAME-001: Daily, weekly, monthly, rolling 3/5/7 are all supported.
/// REQ-TIMEFRAME-004: Every consumer references these same static definitions.
/// REQ-TIMEFRAME-005: Weekly/monthly use pre-computed W_/M_ SQL tables (SqlTablePrefix).
/// REQ-TIMEFRAME-006: Rolling timeframes are computed on demand from D_ (SqlTablePrefix = D_).
/// </summary>
public sealed record Timeframe
{
    /// <summary>Canonical name used in API parameters and persistence (e.g. "daily", "rolling5").</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable label for UI display (e.g. "1D", "5R").</summary>
    public required string DisplayName { get; init; }

    /// <summary>True for rolling 3/5/7; false for daily, weekly, monthly.</summary>
    public bool IsRolling { get; init; }

    /// <summary>Number of sessions for rolling timeframes (3, 5, 7); null otherwise.</summary>
    public int? WindowSize { get; init; }

    /// <summary>
    /// SQL table name prefix: "D_", "W_", "M_".
    /// Rolling timeframes use "D_" (built from daily table per REQ-TIMEFRAME-006).
    /// </summary>
    public required string SqlTablePrefix { get; init; }

    // ── Static definitions ──────────────────────────────────────────────

    public static readonly Timeframe Daily    = new() { Name = "daily",    DisplayName = "1D", SqlTablePrefix = "D_" };
    public static readonly Timeframe Weekly   = new() { Name = "weekly",   DisplayName = "1W", SqlTablePrefix = "W_" };
    public static readonly Timeframe Monthly  = new() { Name = "monthly",  DisplayName = "1M", SqlTablePrefix = "M_" };
    public static readonly Timeframe Rolling3 = new() { Name = "rolling3", DisplayName = "3R", IsRolling = true, WindowSize = 3,  SqlTablePrefix = "D_" };
    public static readonly Timeframe Rolling5 = new() { Name = "rolling5", DisplayName = "5R", IsRolling = true, WindowSize = 5,  SqlTablePrefix = "D_" };
    public static readonly Timeframe Rolling7 = new() { Name = "rolling7", DisplayName = "7R", IsRolling = true, WindowSize = 7,  SqlTablePrefix = "D_" };

    /// <summary>All supported timeframes in display order.</summary>
    public static readonly IReadOnlyList<Timeframe> All = [Daily, Weekly, Monthly, Rolling3, Rolling5, Rolling7];

    private static readonly Dictionary<string, Timeframe> ByName = All.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves a timeframe name (case-insensitive). Returns null for unknown names.</summary>
    public static Timeframe? TryParse(string? name) =>
        name is not null && ByName.TryGetValue(name, out var tf) ? tf : null;

    /// <summary>Resolves a timeframe name (case-insensitive). Throws for unknown names.</summary>
    public static Timeframe Parse(string name) =>
        TryParse(name) ?? throw new ArgumentException(
            $"Unknown timeframe '{name}'. Valid values: {string.Join(", ", All.Select(t => t.Name))}.", nameof(name));
}
