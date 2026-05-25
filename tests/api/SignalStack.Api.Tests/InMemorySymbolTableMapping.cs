using SignalStack.Api.Historical;
using SignalStack.Storage.Historical;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory implementation of <see cref="ISymbolTableMapping"/> for testing.
/// </summary>
public sealed class InMemorySymbolTableMapping : ISymbolTableMapping
{
    private readonly Dictionary<string, string> _symbolToSuffix = new(StringComparer.OrdinalIgnoreCase);

    public InMemorySymbolTableMapping() { }

    public InMemorySymbolTableMapping(params (string Symbol, string Suffix)[] entries)
    {
        foreach (var (symbol, suffix) in entries)
        {
            _symbolToSuffix[symbol] = suffix;
        }
    }

    public Task<(string Daily, string Weekly, string Monthly)?> GetTableNamesAsync(
        string symbol, CancellationToken ct = default)
    {
        if (_symbolToSuffix.TryGetValue(symbol, out var suffix))
            return Task.FromResult<(string, string, string)?>(( $"D_{suffix}", $"W_{suffix}", $"M_{suffix}"));

        return Task.FromResult<(string, string, string)?>(null);
    }

    public Task<string?> GetSuffixAsync(string symbol, CancellationToken ct = default)
    {
        if (_symbolToSuffix.TryGetValue(symbol, out var suffix))
            return Task.FromResult<string?>(suffix);

        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllSuffixesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(_symbolToSuffix, StringComparer.OrdinalIgnoreCase));
    }

    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}
