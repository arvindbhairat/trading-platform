using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory <see cref="ISysConfigRepository"/> for integration tests.
/// Returns the seeded defaults defined in REQ-LEGAL-003 / REQ-LEGAL-001.
/// </summary>
public sealed class InMemorySysConfigRepository : ISysConfigRepository
{
    public long TesterCeiling { get; set; } = 30;
    public string CurrentPhase { get; set; } = "A";
    public string? SeedVersion { get; set; } = "v0.3";

    public Task<long> GetTesterCeilingAsync(CancellationToken ct = default)
        => Task.FromResult(TesterCeiling);

    public Task<string> GetCurrentPhaseAsync(CancellationToken ct = default)
        => Task.FromResult(CurrentPhase);

    public Task<string?> GetSeedVersionAsync(CancellationToken ct = default)
        => Task.FromResult(SeedVersion);
}
