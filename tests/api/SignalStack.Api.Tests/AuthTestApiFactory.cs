using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SignalStack.Api.Tests;

/// <summary>
/// WebApplicationFactory variant that supplements the base Testing configuration
/// with JWT signing credentials so that the test-token endpoint and JWT Bearer
/// validation work without requiring an external key store.
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
                ["SignalStack:Bootstrap:LastKnownGood:CachePath"] = ""
            });
        });

        builder.ConfigureServices(services =>
        {
            var migrationHosted = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                             && d.ImplementationType?.Name == "MongoMigrationHostedService")
                .ToList();

            foreach (var descriptor in migrationHosted)
                services.Remove(descriptor);
        });
    }
}
