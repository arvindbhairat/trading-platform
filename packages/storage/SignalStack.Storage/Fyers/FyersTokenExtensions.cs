using Microsoft.Extensions.DependencyInjection;

namespace SignalStack.Storage.Fyers;

public static class FyersTokenExtensions
{
    public static IServiceCollection AddFyersTokenManagement(this IServiceCollection services)
    {
        services.AddSingleton<IFyersTokenRepository, MongoFyersTokenRepository>();
        return services;
    }
}
