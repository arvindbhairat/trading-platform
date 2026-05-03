using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace SignalStack.Migrations;

public static class MongoMigrationBootstrapExtensions
{
    /// <summary>
    /// Registers the MongoDB client, database, migration runner, and the hosted service that
    /// runs pending migrations at application startup.
    /// </summary>
    /// <param name="connectionString">
    /// MongoDB connection string (Tier-1 bootstrap config).  May be <see langword="null"/> at
    /// registration time when config is not yet available (e.g. in test environments); validation
    /// is deferred to the moment <see cref="IMongoClient"/> is first resolved so that test
    /// factories can remove the migration hosted service before the client is ever created.
    /// </param>
    /// <param name="databaseName">Target database name.</param>
    public static IServiceCollection AddMongoMigrations(
        this IServiceCollection services,
        string? connectionString,
        string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        // Validation is lazy: throws only when IMongoClient is resolved.
        // In production the migration hosted service resolves it at startup.
        // In tests the hosted service is removed before startup, so this factory never runs.
        services.TryAddSingleton<IMongoClient>(_ =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "ConnectionStrings:MongoDb is required (Tier-1 bootstrap config). " +
                    "Set it via environment variable ConnectionStrings__MongoDb or Azure App Configuration.");
            return new MongoClient(connectionString);
        });
        services.TryAddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

        services.AddSingleton<MongoMigrationRunner>();
        services.AddHostedService<MongoMigrationHostedService>();

        return services;
    }
}
