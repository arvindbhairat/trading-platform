using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SignalStack.Worker.Jobs.SloBreachMonitor;

/// <summary>
/// DI registration extensions for the SLO breach notification service.
///
/// REQ-SLO-008: routes SLO breach events to admin notification channels
/// with per-SLO cooldown suppression and metric emission.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddSloBreachMonitor(builder.Configuration);
/// </code>
///
/// Registers:
/// - <see cref="SloBreachMonitorOptions"/> bound to the <c>SloBreachMonitor</c> config section
/// - <see cref="SloBreachNotificationService"/> as a singleton
/// </summary>
public static class SloBreachMonitorExtensions
{
    public static IServiceCollection AddSloBreachMonitor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Bind configuration options ─────────────────────────────
        services
            .AddOptions<SloBreachMonitorOptions>()
            .BindConfiguration(SloBreachMonitorOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Step 2: Register the core service as a singleton ───────────────
        // Singleton is required so the in-memory ConcurrentDictionary cooldown
        // state is shared across all callers within the process.
        services.AddSingleton<SloBreachNotificationService>();

        return services;
    }
}
