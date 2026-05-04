using System.Text.Json;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Options for correlated-industry grouping. The groups map is stored as a
/// JSON string in sys_config under <c>risk.correlated_industry_groups</c>
/// and deserialized on first access (REQ-SIZING-014a).
///
/// Default groups:
///   - Banking &amp; Finance: [Banking, Finance]
///   - Metals: [Metals, Metal Products]
///   - Oil &amp; Gas: [Oil, Gas]
/// </summary>
public sealed class CorrelatedIndustryGroupOptions
{
    public const string SectionName = "Worker:Rme:CorrelatedIndustryGroups";

    /// <summary>
    /// JSON representation of the correlated-industry group map.
    /// Deserialized via <see cref="GetGroups"/>.
    /// </summary>
    public string GroupsJson { get; set; } = DefaultGroupsJson;

    private static readonly IReadOnlyList<CorrelatedIndustryGroup> DefaultGroups =
    [
        new CorrelatedIndustryGroup
        {
            GroupName = "Banking & Finance",
            IndustryNames = ["Banking", "Finance"]
        },
        new CorrelatedIndustryGroup
        {
            GroupName = "Metals",
            IndustryNames = ["Metals", "Metal Products"]
        },
        new CorrelatedIndustryGroup
        {
            GroupName = "Oil & Gas",
            IndustryNames = ["Oil", "Gas"]
        }
    ];

    private static string DefaultGroupsJson =>
        JsonSerializer.Serialize(DefaultGroups);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private IReadOnlyList<CorrelatedIndustryGroup>? _parsed;

    /// <summary>
    /// Deserialize and cache the correlated industry groups from the JSON config.
    /// Returns the default groups when the JSON string is empty or fails to parse.
    /// Thread-safe after construction; the reference-type cache is written once.
    /// </summary>
    public IReadOnlyList<CorrelatedIndustryGroup> GetGroups()
    {
        if (_parsed is not null)
            return _parsed;

        if (string.IsNullOrWhiteSpace(GroupsJson))
            return DefaultGroups;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<CorrelatedIndustryGroup>>(
                GroupsJson, JsonOptions);

            _parsed = parsed?.Count > 0 ? parsed : DefaultGroups;
        }
        catch (JsonException)
        {
            _parsed = DefaultGroups;
        }

        return _parsed;
    }
}
