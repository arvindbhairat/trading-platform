using System.Collections.Concurrent;
using SignalStack.Api.Universe;

namespace SignalStack.Api.Historical;

/// <summary>
/// In-memory symbol-to-table-name mapping loaded from MongoDB <c>symbol_master</c>.
///
/// REQ-HIST-011: single shared mapping — all consumers (charting, backtesting,
/// EODSR) must use this service to resolve SQL Server table names.
/// REQ-HIST-003: full table names are constructed by prepending the timeframe prefix.
/// REQ-HIST-004: tables are referenced using <c>sql_table_name_suffix</c>, not symbol or ISIN.
/// REQ-HIST-008: suffix is stable once assigned.
/// </summary>
public sealed class SymbolTableMappingService : ISymbolTableMapping
{
    private readonly ISymbolMasterRepository _repository;
    private readonly ILogger<SymbolTableMappingService> _logger;

    /// <summary>
    /// Maps symbol -> sql_table_name_suffix.
    /// Thread-safe: read-heavy, write-rare (refresh only on universe sync).
    /// </summary>
    private volatile ConcurrentDictionary<string, string> _symbolToSuffix = new(StringComparer.OrdinalIgnoreCase);

    public SymbolTableMappingService(
        ISymbolMasterRepository repository,
        ILogger<SymbolTableMappingService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<(string Daily, string Weekly, string Monthly)?> GetTableNamesAsync(
        string symbol, CancellationToken ct = default)
    {
        var suffix = await GetSuffixAsync(symbol, ct);
        if (suffix is null) return null;

        return ($"D_{suffix}", $"W_{suffix}", $"M_{suffix}");
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

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing symbol-to-table-name mapping from symbol master...");

        var symbols = await _repository.GetAllAsync(archived: null, ct);

        var newMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            if (!string.IsNullOrWhiteSpace(symbol.SqlTableNameSuffix))
            {
                newMap[symbol.Symbol] = symbol.SqlTableNameSuffix;
            }
        }

        _symbolToSuffix = newMap;
        _logger.LogInformation("Symbol-to-table-name mapping refreshed with {Count} entries.", newMap.Count);
    }

    /// <summary>
    /// Should be called during application startup to load the mapping.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            await RefreshAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load symbol-to-table-name mapping on startup. " +
                "Will retry on first use.");
        }
    }
}
