using Microsoft.Extensions.DependencyInjection;
using SignalStack.Worker.Jobs.EodSuccessMarker;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
/// DI registration extensions for the EOD success marker reader and
/// EODSR sequencing gate.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddEodSequencingGate();
/// </code>
///
/// REQ-MARKET-007: EODSR sequencing gate ensures DataSync has completed
/// before EODSR can start.
/// </summary>
public static class EodSequencingGateExtensions
{
    /// <summary>
    /// Registers:
    /// - <see cref="IEodMarkerReader"/> (singleton) — reads DS success markers
    ///   from the <c>job_runs</c> collection.
    /// - <see cref="EodSequencingGate"/> (singleton) — the gate that EODSR
    ///   calls to verify DataSync completion before starting.
    /// </summary>
    public static IServiceCollection AddEodSequencingGate(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEodMarkerReader, EodMarkerReader>();
        services.AddSingleton<EodSequencingGate>();

        return services;
    }
}
