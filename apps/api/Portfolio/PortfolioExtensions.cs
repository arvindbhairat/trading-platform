using SignalStack.Storage.Portfolio;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration extensions for portfolio and reconciliation services.
///
/// Registers:
/// - <see cref="IManualAdjustmentRepository"/> → <see cref="MongoManualAdjustmentRepository"/>
/// - <see cref="SignalStack.Api.Portfolio.HoldingsService"/> — FIFO-based holdings and PnL computation
///
/// Call from API <c>Program.cs</c>.
/// </summary>
public static class PortfolioExtensions
{
    public static IServiceCollection AddPortfolioServices(this IServiceCollection services)
    {
        services.AddSingleton<IManualAdjustmentRepository, MongoManualAdjustmentRepository>();
        services.AddSingleton<SignalStack.Storage.Portfolio.HoldingsService>();
        services.AddSingleton<SignalStack.Api.Portfolio.PortfolioImpactService>();
        return services;
    }
}
