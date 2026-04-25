using SignalStack.Worker.Observability;
using Xunit;

namespace SignalStack.Worker.Tests;

public sealed class OtlpTelemetryOptionsTests
{
  [Fact]
  public void GetActiveEndpoint_returns_primary_endpoint_by_default()
  {
    var options = new OtlpTelemetryOptions
    {
      PrimaryEndpoint = "http://localhost:4317",
      FallbackEndpoint = "http://localhost:14317",
      UseFallbackEndpoint = false
    };

    Assert.Equal(new Uri("http://localhost:4317"), options.GetActiveEndpoint());
  }

  [Fact]
  public void GetActiveEndpoint_returns_fallback_endpoint_when_enabled()
  {
    var options = new OtlpTelemetryOptions
    {
      PrimaryEndpoint = "http://localhost:4317",
      FallbackEndpoint = "http://localhost:14317",
      UseFallbackEndpoint = true
    };

    Assert.Equal(new Uri("http://localhost:14317"), options.GetActiveEndpoint());
  }
}
