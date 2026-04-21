using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
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
  [InlineData("/healthz")]
  [InlineData("/readyz")]
  public async Task HealthEndpoints_return_200(string path)
  {
    using var client = _factory.CreateClient();
    using var resp = await client.GetAsync(path);

    Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
  }
}

