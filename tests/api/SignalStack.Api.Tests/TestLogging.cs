using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SignalStack.Api.Tests;

public sealed class TestLogSink
{
  private readonly ConcurrentQueue<TestLogEntry> _entries = new();

  public IReadOnlyCollection<TestLogEntry> Entries => _entries.ToArray();

  public void Add(TestLogEntry entry)
  {
    _entries.Enqueue(entry);
  }
}

public sealed record TestLogEntry(
  string Category,
  string Message,
  IReadOnlyDictionary<string, object?> State,
  IReadOnlyDictionary<string, object?> Scope);

public sealed class TestLoggerProvider : ILoggerProvider, ISupportExternalScope
{
  private readonly TestLogSink _sink;
  private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

  public TestLoggerProvider(TestLogSink sink)
  {
    _sink = sink;
  }

  public ILogger CreateLogger(string categoryName)
  {
    return new TestLogger(categoryName, _sink, () => _scopeProvider);
  }

  public void Dispose()
  {
  }

  public void SetScopeProvider(IExternalScopeProvider scopeProvider)
  {
    _scopeProvider = scopeProvider;
  }
}

public sealed class TestLogger : ILogger
{
  private readonly string _category;
  private readonly TestLogSink _sink;
  private readonly Func<IExternalScopeProvider> _scopeProviderAccessor;

  public TestLogger(string category, TestLogSink sink, Func<IExternalScopeProvider> scopeProviderAccessor)
  {
    _category = category;
    _sink = sink;
    _scopeProviderAccessor = scopeProviderAccessor;
  }

  public IDisposable BeginScope<TState>(TState state) where TState : notnull
  {
    return _scopeProviderAccessor().Push(state);
  }

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(
    LogLevel logLevel,
    EventId eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception?, string> formatter)
  {
    var stateValues = ToDictionary(state);
    var scopeValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    _scopeProviderAccessor().ForEachScope((scope, collected) =>
    {
      foreach (var pair in ToDictionary(scope))
      {
        collected[pair.Key] = pair.Value;
      }
    }, scopeValues);

    _sink.Add(new TestLogEntry(
      _category,
      stateValues.TryGetValue("{OriginalFormat}", out var originalFormat)
        ? originalFormat?.ToString() ?? formatter(state, exception)
        : formatter(state, exception),
      stateValues,
      scopeValues));
  }

  private static IReadOnlyDictionary<string, object?> ToDictionary<TState>(TState state)
  {
    if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
    {
      return pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
      ["value"] = state
    };
  }
}
