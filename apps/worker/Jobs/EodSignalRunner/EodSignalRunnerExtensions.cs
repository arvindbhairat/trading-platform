using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Signals.Backtesting;
using SignalStack.Storage.Backtesting;
using SignalStack.Domain.Backtesting;
using SignalStack.Worker.Jobs.EodSignalRunner;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// DI registration extensions for the EOD Signal Runner (EODSR) job.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddEodSignalRunner(builder.Configuration);
/// </code>
///
/// REQ-STRAT-013:  read SQL Server data, evaluate for next-trading-day entry.
/// REQ-STRAT-013a: user-scoped execution with single-pass market data read.
/// REQ-STRAT-027:  atomic-run semantics with db-retry.
/// REQ-STRAT-028:  provenance fields on entry signals.
/// </summary>
public static class EodSignalRunnerExtensions
{
    /// <summary>
    /// Register EOD Signal Runner services for post-market entry-signal generation.
    /// </summary>
    public static IServiceCollection AddEodSignalRunner(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Bind configuration options ──────────────────────────────
        services
            .AddOptions<EodSignalRunnerOptions>()
            .BindConfiguration(EodSignalRunnerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Step 2: Register the signal evaluator registry ──────────────────
        // Discovers all IEntrySignalEvaluator implementations registered in DI.
        services.AddSingleton<ISignalEvaluatorRegistry, SignalEvaluatorRegistry>();

        // ── Step 3: Register the core EODSR service ─────────────────────────
        services.AddSingleton<EodSignalRunnerService>();

        // ── Step 4: Register the BackgroundService worker ───────────────────
        services.AddHostedService<EodSignalRunnerWorker>();

        return services;
    }
}
