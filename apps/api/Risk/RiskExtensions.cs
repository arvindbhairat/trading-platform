using SignalStack.Api.Risk;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration extensions for RME advisory and risk services.
/// P6-T27 — Portal RME advisory panel, channel status, portfolio health.
/// </summary>
public static class RiskExtensions
{
    public static IServiceCollection AddRiskServices(this IServiceCollection services)
    {
        services.AddSingleton<RmeAdvisoryService>();
        services.AddSingleton<RmeChannelStatusService>();
        services.AddSingleton<PortfolioHealthService>();
        return services;
    }
}
