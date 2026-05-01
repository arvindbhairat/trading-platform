namespace SignalStack.Api.Universe;

public static class UniverseExtensions
{
    public static IServiceCollection AddUniverseManagement(this IServiceCollection services)
    {
        services.AddSingleton<ISymbolMasterRepository, MongoSymbolMasterRepository>();
        services.AddSingleton<IUniverseUploadRepository, MongoUniverseUploadRepository>();
        services.AddSingleton<UniverseSyncService>();
        return services;
    }
}
