using Microsoft.Extensions.DependencyInjection;

namespace SignalStack.Storage.Admin;

public static class TradingCalendarExtensions
{
    public static IServiceCollection AddTradingCalendarManagement(this IServiceCollection services)
    {
        services.AddSingleton<ITradingCalendarRepository, MongoTradingCalendarRepository>();
        return services;
    }
}
