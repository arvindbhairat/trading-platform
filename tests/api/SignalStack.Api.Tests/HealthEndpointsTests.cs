using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SignalStack.Api.Tests;

public sealed class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> _factory;

  public HealthEndpointsTests(WebApplicationFactory<Program> factory)
  {
    _factory = factory;
  }

  [Theory]
  [InlineData("/api/v1/healthz")]
  [InlineData("/api/v1/readyz")]
  public async Task HealthEndpoints_return_200(string path)
  {
    using var client = _factory.CreateClient();
    using var resp = await client.GetAsync(path);

    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
  }

  [Fact]
  public async Task Api_root_is_versioned_under_api_v1()
  {
    using var client = _factory.CreateClient();

    using var resp = await client.GetAsync("/api/v1");

    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
  }

  [Fact]
  public async Task Request_logging_includes_service_environment_and_trace_enrichers()
  {
    var sink = new TestLogSink();

    using var factory = _factory.WithWebHostBuilder(builder =>
    {
      builder.UseEnvironment("Testing");
      builder.ConfigureServices(services => services.AddSingleton(sink));
      builder.ConfigureLogging(logging =>
      {
        logging.ClearProviders();
        logging.AddProvider(new TestLoggerProvider(sink));
      });
    });

    using var client = factory.CreateClient();
    using var resp = await client.GetAsync("/api/v1/healthz");

    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

    var requestLog = Assert.Single(sink.Entries, entry => entry.Message == "HTTP {Method} {Path} responded {StatusCode}");

    Assert.Equal("SignalStack.Api", requestLog.Scope["service"]);
    Assert.Equal("Testing", requestLog.Scope["environment"]);
    Assert.False(string.IsNullOrWhiteSpace(requestLog.Scope["trace"]?.ToString()));
  }
}

