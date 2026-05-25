using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SignalStack.Storage.SysConfig;

/// <summary>
/// ASP.NET Core health check that fails when the <c>platform.seed.version</c>
/// sentinel row is absent from <c>sys_config</c> (REQ-CONFIG-010).
/// Prevents the API from serving traffic when the seeder step was skipped.
/// </summary>
public sealed class SeedVersionHealthCheck : IHealthCheck
{
    private readonly ISysConfigRepository _sysConfig;

    public SeedVersionHealthCheck(ISysConfigRepository sysConfig)
    {
        _sysConfig = sysConfig;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        var version = await _sysConfig.GetSeedVersionAsync(ct);
        return version is null
            ? HealthCheckResult.Unhealthy(
                "sys_config is missing platform.seed.version — " +
                "the seeder step has not been run. " +
                "Deployment pipeline must run the seeder before activating the API.")
            : HealthCheckResult.Healthy(
                $"platform.seed.version = {version}");
    }
}
