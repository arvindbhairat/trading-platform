using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Worker.Jobs.AdminFyersTokenCheck;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// DI registration extensions for the Admin FYERS Token Validity Check job.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddAdminFyersTokenCheck(builder.Configuration);
/// </code>
///
/// REQ-NOTIFY-022:  daily 15:00 IST DataSync-window check.
/// REQ-NOTIFY-022b: pre-market 08:30 IST check.
/// </summary>
public static class AdminFyersTokenCheckExtensions
{
    /// <summary>
    /// Register Admin FYERS Token Validity Check services.
    /// </summary>
    public static IServiceCollection AddAdminFyersTokenCheck(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Bind configuration options ─────────────────────────────
        services
            .AddOptions<AdminFyersTokenCheckOptions>()
            .BindConfiguration(AdminFyersTokenCheckOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Step 2: Register the core check service ────────────────────────
        services.AddSingleton<AdminFyersTokenCheckService>();

        // ── Step 3: Register the BackgroundService worker ──────────────────
        services.AddHostedService<AdminFyersTokenCheckWorker>();

        return services;
    }
}
