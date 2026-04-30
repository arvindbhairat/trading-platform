namespace SignalStack.Api.SysConfig;

public interface ISysConfigRepository
{
    // Returns operations.phase_a.tester_ceiling; defaults to 30 when not yet seeded.
    Task<long> GetTesterCeilingAsync(CancellationToken ct = default);

    // Returns operations.phase.current; defaults to "A" when not yet seeded.
    Task<string> GetCurrentPhaseAsync(CancellationToken ct = default);

    // Returns platform.seed.version; null when not yet seeded (REQ-CONFIG-010).
    // Used by the sentinel startup check — API/Worker fail fast when absent.
    Task<string?> GetSeedVersionAsync(CancellationToken ct = default);
}
