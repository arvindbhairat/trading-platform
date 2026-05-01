using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Api.Fyers;
using SignalStack.Api.PrivacyRequest;
using SignalStack.Api.Sessions;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Universe;
using SignalStack.Api.Users;

namespace SignalStack.Api.Tests;

/// <summary>
/// <see cref="WebApplicationFactory{TProgram}"/> that provides an in-memory
/// symbol master repository seeded with test data, plus all the standard
/// auth infrastructure (JWT signing, in-memory sessions, users, etc.)
/// so that authenticated universe endpoints can be exercised.
/// </summary>
public sealed class SymbolMasterTestFactory : WebApplicationFactory<Program>
{
    internal const string TestJwtSecret =
        "signalstack-test-secret-do-not-use-in-production-must-be-at-least-32-chars";

    /// <summary>
    /// Requests a test admin JWT from the factory's test-token endpoint.
    /// </summary>
    internal static async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        using var resp = await client.GetAsync("/api/v1/auth/test-token");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TestTokenResponse>();
        return body!.token;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoDb"] = "mongodb://test-stub:27017/signalstack-test",
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

            // Replace MongoDB-backed repositories with in-memory implementations.
            ReplaceService<ISessionRepository>(services,
                new ServiceDescriptor(typeof(ISessionRepository),
                    typeof(InMemorySessionRepository), ServiceLifetime.Singleton));

            ReplaceService<IUserRepository>(services,
                new ServiceDescriptor(typeof(IUserRepository),
                    typeof(InMemoryUserRepository), ServiceLifetime.Singleton));

            ReplaceService<ISysConfigRepository>(services,
                new ServiceDescriptor(typeof(ISysConfigRepository),
                    typeof(InMemorySysConfigRepository), ServiceLifetime.Singleton));

            ReplaceService<IFyersTokenRepository>(services,
                new ServiceDescriptor(typeof(IFyersTokenRepository),
                    typeof(InMemoryFyersTokenRepository), ServiceLifetime.Singleton));

            ReplaceService<IAuditEventRepository>(services,
                new ServiceDescriptor(typeof(IAuditEventRepository),
                    typeof(InMemoryAuditEventRepository), ServiceLifetime.Singleton));

            ReplaceService<IPrivacyRequestRepository>(services,
                new ServiceDescriptor(typeof(IPrivacyRequestRepository),
                    typeof(InMemoryPrivacyRequestRepository), ServiceLifetime.Singleton));

            // Replace the MongoDB-backed symbol master repository with an in-memory
            // implementation seeded with test data.
            ReplaceService<ISymbolMasterRepository>(services,
                new ServiceDescriptor(typeof(ISymbolMasterRepository),
                    _ => CreateSeededRepository(), ServiceLifetime.Singleton));
        });
    }

    private static InMemorySymbolMasterRepository CreateSeededRepository()
    {
        var repo = new InMemorySymbolMasterRepository();
        var now = DateTime.UtcNow;

        // Seed a few symbols for test queries.
        foreach (var seed in new[] {
            ("RELIANCE", "Reliance Industries", "Oil & Gas", "EQ", "INE002A01018"),
            ("TCS",       "Tata Consultancy Services", "IT Services", "EQ", "INE467B01029"),
            ("INFY",      "Infosys Limited", "IT Services", "EQ", "INE009A01021"),
            ("HDFCBANK",  "HDFC Bank", "Banking", "EQ", "INE040A01034"),
        })
        {
            var suffix = SqlTableNameSuffixGenerator.Generate(seed.Item1);
            repo.CreateAsync(new SymbolMasterDocument
            {
                Id = ObjectId.GenerateNewId(),
                Symbol = seed.Item1,
                CompanyName = seed.Item2,
                Industry = seed.Item3,
                Series = seed.Item4,
                Isin = seed.Item5,
                SqlTableNameSuffix = suffix,
                LotSize = 1,
                IsArchived = false,
                ScanExcluded = false,
                CreatedAt = now,
                UpdatedAt = now
            }).GetAwaiter().GetResult();
        }

        return repo;
    }

    private static void ReplaceService<T>(IServiceCollection services, ServiceDescriptor replacement)
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing is not null) services.Remove(existing);
        services.Add(replacement);
    }

    private sealed record TestTokenResponse(string token);
}
