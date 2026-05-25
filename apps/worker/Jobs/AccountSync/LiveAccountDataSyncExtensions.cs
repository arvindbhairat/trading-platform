using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Domain.Execution;
using SignalStack.Storage.Execution;
using SignalStack.Worker.Jobs.AccountSync;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// DI registration extensions for the Live Account Data Sync (LADS) job.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddAccountSync(builder.Configuration);
/// </code>
///
/// REQ-PORT-019:  intraday recurring sync during market hours.
/// REQ-PORT-021a: cycle-level abort tracking + graduated suspension.
/// </summary>
public static class LiveAccountDataSyncExtensions
{
    /// <summary>
    /// Register Live Account Data Sync (LADS) services.
    /// </summary>
    public static IServiceCollection AddAccountSync(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Bind configuration options ─────────────────────────────
        services
            .AddOptions<AccountSyncOptions>()
            .BindConfiguration(AccountSyncOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Step 2: Register the core sync service ─────────────────────────
        services.AddSingleton<LiveAccountDataSyncService>();

        // ── Step 2a: Register intent-ledger repository for reconciliation ──
        // P7-T8 / REQ-ORDER-015c: the worker needs IIntentLedgerRepository
        // for the LADS intent-reconciliation safety net.
        services.AddSingleton<IIntentLedgerRepository, MongoIntentLedgerRepository>();

        // ── Step 2b: Register the intent-reconciliation safety net ─────────
        // P7-T8 / REQ-ORDER-015c: LADS intent-reconciliation per LADS cycle.
        services.AddSingleton<IntentReconciliationService>();

        // ── Step 3: Register the BackgroundService worker ──────────────────
        services.AddHostedService<LiveAccountDataSyncWorker>();

        return services;
    }
}
