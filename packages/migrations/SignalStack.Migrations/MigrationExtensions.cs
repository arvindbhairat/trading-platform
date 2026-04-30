using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SignalStack.Migrations;

public static class MigrationExtensions
{
    public static IServiceCollection AddMongoMigrations(
        this IServiceCollection services,
        string? mongoConnectionString,
        string? mongoDatabaseName)
    {
        services.TryAddSingleton<MongoMigrationHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<MongoMigrationHostedService>());
        return services;
    }
}
