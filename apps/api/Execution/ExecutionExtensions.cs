using SignalStack.Api.Execution;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for execution assistance services (Phase 7).
/// P7-T3: PreFlightCheckService.
/// </summary>
public static class ExecutionExtensions
{
    public static IServiceCollection AddExecutionServices(this IServiceCollection services)
    {
        services.AddSingleton<PreFlightCheckService>();
        return services;
    }
}
