namespace SignalStack.Api.PrivacyRequest;

public static class PrivacyRequestExtensions
{
    public static IServiceCollection AddPrivacyRequestManagement(this IServiceCollection services)
    {
        services.AddSingleton<IPrivacyRequestRepository, MongoPrivacyRequestRepository>();
        return services;
    }
}
