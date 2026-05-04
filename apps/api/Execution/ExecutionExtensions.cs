using SignalStack.Api.Execution;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for execution assistance services (Phase 7).
/// P7-T3: PreFlightCheckService.
/// P7-T4: OrderContextService.
/// </summary>
public static class ExecutionExtensions
{
    public static IServiceCollection AddExecutionServices(this IServiceCollection services)
    {
        services.AddSingleton<PreFlightCheckService>();
        services.AddSingleton<OrderContextService>();
        return services;
    }
}
