using SignalStack.Api.Observability;
using Xunit;

namespace SignalStack.Api.Tests;

public sealed class OtlpTelemetryOptionsTests
{
  [Fact]
  public void Endpoint_defaults_to_empty()
  {
    var options = new OtlpTelemetryOptions();

    Assert.Equal(string.Empty, options.Endpoint);
  }

  [Fact]
  public void ApiKey_defaults_to_empty()
  {
    var options = new OtlpTelemetryOptions();

    Assert.Equal(string.Empty, options.ApiKey);
  }

  [Fact]
  public void ExportTimeout_defaults_to_5000()
  {
    var options = new OtlpTelemetryOptions();

    Assert.Equal(5000, options.ExportTimeoutMilliseconds);
  }
}
