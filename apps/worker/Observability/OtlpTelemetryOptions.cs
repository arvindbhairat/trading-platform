namespace SignalStack.Worker.Observability;

public sealed class OtlpTelemetryOptions
{
  public const string SectionName = "Telemetry:Otlp";

  public string Endpoint { get; set; } = string.Empty;
  public string ApiKey { get; set; } = string.Empty;
  public int ExportTimeoutMilliseconds { get; set; } = 5000;
}
