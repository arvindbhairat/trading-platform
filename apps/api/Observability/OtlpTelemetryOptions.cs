namespace SignalStack.Api.Observability;

public sealed class OtlpTelemetryOptions
{
  public const string SectionName = "Telemetry:Otlp";
  private const string DefaultPrimaryEndpoint = "http://localhost:4317";
  private const string DefaultFallbackEndpoint = "http://localhost:14317";

  public string PrimaryEndpoint { get; set; } = DefaultPrimaryEndpoint;
  public string FallbackEndpoint { get; set; } = DefaultFallbackEndpoint;
  public bool UseFallbackEndpoint { get; set; }
  public int ExportTimeoutMilliseconds { get; set; } = 5000;

  public Uri GetActiveEndpoint()
  {
    var selectedEndpoint = UseFallbackEndpoint && !string.IsNullOrWhiteSpace(FallbackEndpoint)
      ? FallbackEndpoint
      : PrimaryEndpoint;

    return new Uri(selectedEndpoint, UriKind.Absolute);
  }
}
