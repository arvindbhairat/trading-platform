using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignalStack.Api.Admin;
using SignalStack.Api.Audit;
using SignalStack.Api.Fyers;
using SignalStack.Api.Historical;
using SignalStack.Api.PrivacyRequest;
using SignalStack.Api.Sessions;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Users;

namespace SignalStack.Api.Tests;

/// <summary>
/// WebApplicationFactory variant that supplements the base Testing configuration
/// with JWT signing credentials and an in-memory session repository so that the
/// test-token endpoint, JWT Bearer validation, and session-validation middleware
/// all work without requiring a live MongoDB.
/// </summary>
public sealed class AuthTestApiFactory : WebApplicationFactory<Program>
{
    // 512-bit test secret — used only in the Testing environment.
    internal const string TestJwtSecret =
        "signalstack-test-secret-do-not-use-in-production-must-be-at-least-32-chars";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoDb"] =
                    "mongodb://test-stub:27017/signalstack-test",
                ["Auth:Jwt:Secret"] = TestJwtSecret,
                ["Auth:Jwt:Issuer"] = "signalstack-api",
                ["Auth:Jwt:Audience"] = "signalstack-portal",
                ["Auth:Jwt:ExpiryMinutes"] = "60",
                ["Auth:FrontendBaseUrl"] = "http://localhost:3000",
                ["Auth:SeedAdminEmail"] = "admin@signalstack.test",
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

            // Replace the MongoDB-backed session repository with an in-memory
            // implementation so the session-validation middleware works in tests
            // without a live MongoDB connection (REQ-SESSION-002a).
            var sessionDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ISessionRepository));
            if (sessionDescriptor is not null)
                services.Remove(sessionDescriptor);

            services.AddSingleton<ISessionRepository, InMemorySessionRepository>();

            // Replace MongoDB-backed user and sys_config repositories.
            ReplaceService<IUserRepository>(services,
                new ServiceDescriptor(typeof(IUserRepository),
                    typeof(InMemoryUserRepository), ServiceLifetime.Singleton));

            ReplaceService<ISysConfigRepository>(services,
                new ServiceDescriptor(typeof(ISysConfigRepository),
                    typeof(InMemorySysConfigRepository), ServiceLifetime.Singleton));

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

            // Replace the MongoDB-backed FYERS token repository with an in-memory
            // implementation so the session/status FYERS token check works in tests.
            ReplaceService<IFyersTokenRepository>(services,
                new ServiceDescriptor(typeof(IFyersTokenRepository),
                    typeof(InMemoryFyersTokenRepository), ServiceLifetime.Singleton));

            // Replace the MongoDB-backed symbol table mapping with an in-memory
            // implementation so startup initialization does not require a live MongoDB.
            // InitializeHistoricalServicesAsync skips initialization when it detects
            // a non-SymbolTableMappingService implementation.
            ReplaceService<ISymbolTableMapping>(services,
                new ServiceDescriptor(typeof(ISymbolTableMapping),
                    typeof(InMemorySymbolTableMapping), ServiceLifetime.Singleton));

            // Replace the MongoDB-backed trading calendar repository with an in-memory
            // implementation so PhaseConstraintService calendar coverage checks do not
            // require a live MongoDB (REQ-CALENDAR-007).
            ReplaceService<ITradingCalendarRepository>(services,
                new ServiceDescriptor(typeof(ITradingCalendarRepository),
                    typeof(InMemoryTradingCalendarRepository), ServiceLifetime.Singleton));

            // Replace the MongoDB-backed chaos exercise repository with an in-memory
            // implementation (P8-T12 / REQ-NFR-014).
            ReplaceService<IChaosExerciseRepository>(services,
                new ServiceDescriptor(typeof(IChaosExerciseRepository),
                    typeof(InMemoryChaosExerciseRepository), ServiceLifetime.Singleton));
        });
    }

    private static void ReplaceService<T>(IServiceCollection services, ServiceDescriptor replacement)
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing is not null) services.Remove(existing);
        services.Add(replacement);
    }
}
