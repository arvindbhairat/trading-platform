using System.Text.Json.Serialization;

namespace SignalStack.Worker.Rme;

/// <summary>
/// A correlated-industry group: industry names that are treated as a single
/// combined sector for concentration-limit purposes under REQ-SIZING-014 and
/// REQ-RME-017. Stored in sys_config as <c>risk.correlated_industry_groups</c>
/// and deserialized from the JSON representation defined in REQ-SIZING-014a.
/// </summary>
public sealed record CorrelatedIndustryGroup
{
    /// <summary>Display name for the group (e.g. "Banking & Finance").</summary>
    [JsonPropertyName("group_name")]
    public required string GroupName { get; init; }

    /// <summary>
    /// Industry names that are treated as correlated. Each value must match
    /// the <c>Industry</c> field in the symbol master.
    /// </summary>
    [JsonPropertyName("industry_names")]
    public required IReadOnlyList<string> IndustryNames { get; init; }
}
