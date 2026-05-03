using Microsoft.Extensions.DependencyInjection;
using SignalStack.Worker.Integrations.Fyers;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// DI registration for <see cref="SharedTokenHealthService"/>.
/// </summary>
public static class SharedTokenHealthServiceExtensions
{
    public static IServiceCollection AddSharedTokenHealthService(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<SharedTokenHealthService>();

        return services;
    }
}
