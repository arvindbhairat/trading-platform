using System.Text.Json;

namespace SignalStack.Seed;

/// <summary>
/// Structured report artefact emitted by the seeder for pipeline capture.
/// </summary>
public sealed record SeedResultReport(
    string SeedVersion,
    DateTime Timestamp,
    IReadOnlyList<string> InsertedKeys,
    IReadOnlyList<string> SkippedExistingKeys,
    IReadOnlyList<string> PerDeploymentResolved,
    IReadOnlyList<string> PerDeploymentMissing,
    IReadOnlyList<string> ValidationErrors,
    string? SentinelKey,
    string? SentinelValue)
{
    public int InsertedCount => InsertedKeys.Count;
    public int SkippedCount => SkippedExistingKeys.Count;
    public int ErrorCount => ValidationErrors.Count;
    public int PerDeploymentResolvedCount => PerDeploymentResolved.Count;
    public int PerDeploymentMissingCount => PerDeploymentMissing.Count;
    public int TotalManifestKeys => InsertedCount + SkippedCount + ErrorCount + PerDeploymentMissingCount;

    public string ToJson()
    {
        var obj = new
        {
            seedVersion = SeedVersion,
            timestamp = Timestamp.ToString("O"),
            summary = new
            {
                totalKeysInManifest = TotalManifestKeys,
                inserted = InsertedCount,
                skippedExisting = SkippedCount,
                errors = ErrorCount,
                perDeploymentResolved = PerDeploymentResolvedCount,
                perDeploymentMissing = PerDeploymentMissingCount,
            },
            details = new
            {
                inserted = InsertedKeys,
                skipped = SkippedExistingKeys,
                perDeploymentResolvedKeys = PerDeploymentResolved,
                perDeploymentMissingKeys = PerDeploymentMissing,
                validationErrors = ValidationErrors,
            },
            sentinel = SentinelKey is not null
                ? new { key = SentinelKey, value = SentinelValue }
                : null,
        };

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}
