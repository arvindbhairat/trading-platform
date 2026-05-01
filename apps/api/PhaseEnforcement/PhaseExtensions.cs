namespace SignalStack.Api.PhaseEnforcement;

/// <summary>
/// DI registration for Phase A constraint enforcement services (REQ-LEGAL-002).
/// </summary>
public static class PhaseExtensions
{
    public static IServiceCollection AddPhaseConstraintServices(
        this IServiceCollection services)
    {
        services.AddSingleton<PhaseConstraintService>();
        return services;
    }
}
