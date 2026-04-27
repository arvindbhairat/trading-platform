using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SignalStack.Configuration.Logging;
using Xunit;

namespace SignalStack.Api.Tests;

public sealed class SensitiveDataRedactionTests
{
  [Fact]
  public void Serilog_enricher_redacts_sensitive_properties_before_they_hit_a_sink()
  {
    var sink = new CollectingSerilogSink();

    using var logger = new LoggerConfiguration()
      .Enrich.With<SensitiveDataRedactionEnricher>()
      .WriteTo.Sink(sink)
      .CreateLogger();

    logger.Information("Received token {access_token} from {source}", "secret-value", "fyers");

    var logEvent = Assert.Single(sink.Events);
    var redactedProperty = Assert.IsType<ScalarValue>(logEvent.Properties["access_token"]);
    var preservedProperty = Assert.IsType<ScalarValue>(logEvent.Properties["source"]);

    Assert.Equal(SensitiveDataRedaction.RedactedValue, redactedProperty.Value);
    Assert.Equal("fyers", preservedProperty.Value);
  }

  [Fact]
  public void OpenTelemetry_processor_redacts_sensitive_attributes_before_export()
  {
    var exporter = new CollectingLogRecordExporter();

    using var loggerFactory = LoggerFactory.Create(builder =>
    {
      builder.AddOpenTelemetry(logging =>
      {
        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = true;
        logging.ParseStateValues = true;
        logging.AddProcessor(new SensitiveDataRedactionLogProcessor());
        logging.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
      });
    });

    var logger = loggerFactory.CreateLogger("SensitiveDataRedactionTests");
    logger.LogInformation("Received token {access_token} from {source}", "secret-value", "fyers");

    var exportedRecord = Assert.Single(exporter.ExportedAttributes);
    Assert.Equal(SensitiveDataRedaction.RedactedValue, exportedRecord["access_token"]);
    Assert.Equal("fyers", exportedRecord["source"]);
  }

  private sealed class CollectingSerilogSink : ILogEventSink
  {
    private readonly List<LogEvent> _events = [];

    public IReadOnlyList<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent)
    {
      _events.Add(logEvent);
    }
  }

  private sealed class CollectingLogRecordExporter : BaseExporter<LogRecord>
  {
    private readonly List<IReadOnlyDictionary<string, object?>> _exportedAttributes = [];

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ExportedAttributes => _exportedAttributes;

    public override ExportResult Export(in Batch<LogRecord> batch)
    {
      foreach (var logRecord in batch)
      {
        _exportedAttributes.Add((logRecord.Attributes ?? [])
          .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.OrdinalIgnoreCase));
      }

      return ExportResult.Success;
    }
  }
}
