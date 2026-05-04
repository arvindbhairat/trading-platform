using SignalStack.Api.Execution;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for execution assistance services (Phase 7).
/// P7-T3: PreFlightCheckService.
/// P7-T4: OrderContextService.
/// P7-T5: IntentSigningService.
/// </summary>
public static class ExecutionExtensions
{
    public static IServiceCollection AddExecutionServices(this IServiceCollection services)
    {
        services.AddSingleton<PreFlightCheckService>();
        services.AddSingleton<OrderContextService>();
        services.AddSingleton<IIntentSigningService, IntentSigningService>();
        return services;
    }
}
