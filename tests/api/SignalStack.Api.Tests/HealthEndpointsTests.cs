using System.Net;
using System.Net.Http.Json;
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
  public async Task Api_responses_include_security_headers()
  {
    using var client = _factory.CreateClient();

    using var resp = await client.GetAsync("/api/v1");

    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    Assert.Equal("DENY", resp.Headers.GetValues("X-Frame-Options").Single());
    Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
    Assert.Equal("strict-origin-when-cross-origin", resp.Headers.GetValues("Referrer-Policy").Single());
    Assert.Equal("camera=(), microphone=(), geolocation=()", resp.Headers.GetValues("Permissions-Policy").Single());

    var csp = resp.Headers.GetValues("Content-Security-Policy").Single();
    Assert.Contains("default-src 'none'", csp);
    Assert.Contains("frame-ancestors 'none'", csp);
  }

  [Fact]
  public async Task Auth_probe_rate_limits_after_threshold()
  {
    using var client = _factory.CreateClient();

    for (var attempt = 0; attempt < 10; attempt++)
    {
      using var okResponse = await client.PostAsJsonAsync("/api/v1/auth/probe", new { });
      Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }

    using var rateLimitedResponse = await client.PostAsJsonAsync("/api/v1/auth/probe", new { });

    Assert.Equal((HttpStatusCode)429, rateLimitedResponse.StatusCode);
    Assert.Equal("60", rateLimitedResponse.Headers.GetValues("Retry-After").Single());
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

