using MongoDB.Bson;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory <see cref="ISysConfigRepository"/> for integration tests.
/// Supports the seeded defaults and the legal version keys for P2-T14.
/// </summary>
public sealed class InMemorySysConfigRepository : ISysConfigRepository
{
    public long TesterCeiling { get; set; } = 30;
    public string CurrentPhase { get; set; } = "A";
    public string? SeedVersion { get; set; } = "v0.3";

    private readonly Dictionary<string, string> _values = new()
    {
        ["legal.tos.current_version"] = "v1",
        ["legal.privacy.current_version"] = "v1",
        ["legal.tester_acknowledgement.current_version"] = "v1",
        ["legal.disclaimer.long_version"] = "v1",
    };

    public Task<long> GetTesterCeilingAsync(CancellationToken ct = default)
        => Task.FromResult(TesterCeiling);

    public Task<string> GetCurrentPhaseAsync(CancellationToken ct = default)
        => Task.FromResult(CurrentPhase);

    public Task<string?> GetSeedVersionAsync(CancellationToken ct = default)
        => Task.FromResult(SeedVersion);

    public Task<BsonDocument?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        if (_values.TryGetValue(key, out var value))
        {
            return Task.FromResult<BsonDocument?>(new BsonDocument
            {
                ["key"] = key,
                ["value"] = new BsonString(value),
                ["category"] = "legal",
                ["valueType"] = "string"
            });
        }

        return Task.FromResult<BsonDocument?>(null);
    }

    public Task<List<BsonDocument>> ListAllAsync(string? category = null, CancellationToken ct = default)
        => Task.FromResult(new List<BsonDocument>());

    public Task<BsonDocument?> UpdateAsync(string key, BsonValue newValue, string updatedByUserId, CancellationToken ct = default)
        => Task.FromResult<BsonDocument?>(null);

    public Task<BsonDocument?> ResetToDefaultAsync(string key, string updatedByUserId, CancellationToken ct = default)
        => Task.FromResult<BsonDocument?>(null);

    public Task<List<string>> ListCategoriesAsync(CancellationToken ct = default)
        => Task.FromResult(new List<string>());

    public Task<decimal> GetDecimalAsync(string key, decimal defaultValue, CancellationToken ct = default)
        => Task.FromResult(defaultValue);

    public Task<long> GetLongAsync(string key, long defaultValue, CancellationToken ct = default)
        => Task.FromResult(defaultValue);
}
