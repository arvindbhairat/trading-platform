using Microsoft.Extensions.DependencyInjection;
using SignalStack.Domain.Audit;

namespace SignalStack.Storage.Audit;

public static class AuditExtensions
{
    public static IServiceCollection AddAuditEventManagement(this IServiceCollection services)
    {
        services.AddSingleton<IAuditEventRepository, MongoAuditEventRepository>();
        return services;
    }
}
