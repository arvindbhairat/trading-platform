using SignalStack.Domain.Audit;

namespace SignalStack.Api.Audit;

public static class AuditExtensions
{
    public static IServiceCollection AddAuditEventManagement(this IServiceCollection services)
    {
        services.AddSingleton<IAuditEventRepository, MongoAuditEventRepository>();
        return services;
    }
}
