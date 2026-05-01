using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignalStack.Api.Audit;
using SignalStack.Api.Fyers;
using SignalStack.Api.PrivacyRequest;
using SignalStack.Api.Sessions;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Users;

namespace SignalStack.Api.Tests;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TProgram}"/> that:
/// <list type="bullet">
///   <item>Runs in the "Testing" environment.</item>
///   <item>Supplies a stub MongoDB connection string so the Tier-1 bootstrap check passes.</item>
///   <item>Removes the <c>MongoMigrationHostedService</c> so tests do not require a live MongoDB.</item>
///   <item>Replaces <c>ISessionRepository</c> with an in-memory implementation so the
///         session-validation middleware works without a live MongoDB (REQ-SESSION-002a).</item>
/// </list>
/// The IMongoClient/IMongoDatabase singletons remain registered (MongoDB.Driver is lazy
/// and will not attempt a connection until the first call); the hosted service is the only
/// component that makes an actual connection at startup.
/// </summary>
public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoDb"] = "mongodb://test-stub:27017/signalstack-test",
                ["Auth:Jwt:Secret"] = AuthTestApiFactory.TestJwtSecret,
                ["Auth:Jwt:Issuer"] = "signalstack-api",
                ["Auth:Jwt:Audience"] = "signalstack-portal",
                ["Auth:FrontendBaseUrl"] = "http://localhost:3000",
                // Disable LKG caching in tests to prevent a race condition when
                // multiple test factories start concurrently and both try to write
                // the same cache file.
                ["SignalStack:Bootstrap:LastKnownGood:CachePath"] = ""
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the migration hosted service so no live MongoDB is needed.
            var migrationHosted = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                             && d.ImplementationType?.Name == "MongoMigrationHostedService")
                .ToList();

            foreach (var descriptor in migrationHosted)
                services.Remove(descriptor);

            // Replace the MongoDB-backed session repository so the session-validation
            // middleware can run without a live MongoDB connection.
            var sessionDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ISessionRepository));
            if (sessionDescriptor is not null)
                services.Remove(sessionDescriptor);

            services.AddSingleton<ISessionRepository, InMemorySessionRepository>();

            ReplaceService<IUserRepository>(services,
                new ServiceDescriptor(typeof(IUserRepository),
                    typeof(InMemoryUserRepository), ServiceLifetime.Singleton));

            ReplaceService<ISysConfigRepository>(services,
                new ServiceDescriptor(typeof(ISysConfigRepository),
                    typeof(InMemorySysConfigRepository), ServiceLifetime.Singleton));

            ReplaceService<IFyersTokenRepository>(services,
                new ServiceDescriptor(typeof(IFyersTokenRepository),
                    typeof(InMemoryFyersTokenRepository), ServiceLifetime.Singleton));

            // Replace the MongoDB-backed audit event repository with an in-memory
            // implementation for DSAR integration tests (REQ-PRIVACY-004 / P2-T21).
            ReplaceService<IAuditEventRepository>(services,
                new ServiceDescriptor(typeof(IAuditEventRepository),
                    typeof(InMemoryAuditEventRepository), ServiceLifetime.Singleton));

            // Replace the MongoDB-backed privacy request repository with an in-memory
            // implementation for DSAR integration tests (REQ-PRIVACY-004 / P2-T21).
            ReplaceService<IPrivacyRequestRepository>(services,
                new ServiceDescriptor(typeof(IPrivacyRequestRepository),
                    typeof(InMemoryPrivacyRequestRepository), ServiceLifetime.Singleton));
        });
    }

    private static void ReplaceService<T>(IServiceCollection services, ServiceDescriptor replacement)
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing is not null) services.Remove(existing);
        services.Add(replacement);
    }
}
