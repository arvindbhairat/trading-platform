using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Storage.SysConfig;

namespace SignalStack.Storage.Universe;

/// <summary>
/// Orchestrates the Symbol Validity Probe: fetches LTP for all active symbols via
/// <see cref="ISymbolProbeClient"/>, classifies outcomes per REQ-UNIV-021a, updates
/// per-symbol health fields, and creates a <c>job_runs</c> record.
/// </summary>
public sealed class SymbolProbeService
{
    private const string ProbeEnabledKey = "operations.universe.symbol_probe_enabled";
    private const string BatchSizeKey = "operations.universe.symbol_probe_batch_size";
    private const string FlagThresholdKey = "operations.universe.symbol_probe_flag_threshold_days";

    private readonly ISymbolMasterRepository _symbolRepo;
    private readonly ISymbolProbeClient _probeClient;
    private readonly ISysConfigRepository _configRepo;
    private readonly IMongoDatabase _database;
    private readonly ILogger<SymbolProbeService> _logger;

    public SymbolProbeService(
        ISymbolMasterRepository symbolRepo,
        ISymbolProbeClient probeClient,
        ISysConfigRepository configRepo,
        IMongoDatabase database,
        ILogger<SymbolProbeService> logger)
    {
        _symbolRepo = symbolRepo;
        _probeClient = probeClient;
        _configRepo = configRepo;
        _database = database;
        _logger = logger;
    }

    /// <summary>
    /// Runs the probe for all active symbols. Returns a summary or null if disabled.
    /// </summary>
    public async Task<ProbeRunSummary?> RunProbeAsync(
        string triggeredBy, string? actorId, CancellationToken ct = default)
    {
        var enabledDoc = await _configRepo.GetByKeyAsync(ProbeEnabledKey, ct);
        var enabled = enabledDoc?.GetValue("value", true) is BsonValue bv && bv.IsBoolean && bv.AsBoolean;
        if (!enabled)
        {
            _logger.LogInformation("Symbol probe is disabled via sys_config ({Key}). Skipping.", ProbeEnabledKey);
            return null;
        }

        var batchSizeDoc = await _configRepo.GetByKeyAsync(BatchSizeKey, ct);
        var batchSize = batchSizeDoc?.GetValue("value", 50) is BsonValue bvSize && bvSize.IsInt32 ? bvSize.AsInt32 : 50;

        var thresholdDoc = await _configRepo.GetByKeyAsync(FlagThresholdKey, ct);
        var flagThreshold = thresholdDoc?.GetValue("value", 2) is BsonValue bvThresh && bvThresh.IsInt32 ? bvThresh.AsInt32 : 2;

        var symbols = await _symbolRepo.GetAllAsync(archived: false, ct);
        if (symbols.Count == 0)
        {
            _logger.LogInformation("Symbol probe: no active symbols to probe.");
            return new ProbeRunSummary(0, 0, 0, 0, null);
        }

        var now = DateTime.UtcNow;
        var totalSuccess = 0;
        var totalTransient = 0;
        var totalUnknown = 0;

        for (var i = 0; i < symbols.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) break;

            var batch = symbols.Skip(i).Take(batchSize).ToList();
            var healthUpdates = new List<(SymbolMasterDocument Symbol, ProbeResult Result)>();

            foreach (var symbol in batch)
            {
                ProbeResult result;
                try
                {
                    result = await _probeClient.ProbeAsync(symbol.Symbol, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Symbol probe transient failure for {Symbol}: {Message}",
                        symbol.Symbol, ex.Message);
                    result = new ProbeResult(ProbeOutcome.Transient, ex.GetType().Name);
                }

                healthUpdates.Add((symbol, result));

                switch (result.Outcome)
                {
                    case ProbeOutcome.Success:
                        totalSuccess++;
                        break;
                    case ProbeOutcome.Transient:
                        totalTransient++;
                        break;
                    case ProbeOutcome.UnknownSymbol:
                        totalUnknown++;
                        break;
                }
            }

            var updateTasks = healthUpdates.Select(hu =>
            {
                var (symbol, result) = hu;
                return result.Outcome switch
                {
                    ProbeOutcome.Success => _symbolRepo.UpdateSymbolHealthAsync(
                        symbol.Id,
                        consecutiveFailureCount: 0,
                        lastSuccessfulProbeAt: now,
                        ct: ct),
                    ProbeOutcome.Transient => _symbolRepo.UpdateSymbolHealthAsync(
                        symbol.Id,
                        ct: ct),
                    ProbeOutcome.UnknownSymbol => _symbolRepo.UpdateSymbolHealthAsync(
                        symbol.Id,
                        consecutiveFailureCount: symbol.ConsecutiveFailureCount + 1,
                        lastUnknownSymbolAt: now,
                        ct: ct),
                    _ => Task.CompletedTask
                };
            });

            await Task.WhenAll(updateTasks);
        }

        var flaggedSymbols = symbols
            .Where(s => s.ConsecutiveFailureCount >= flagThreshold)
            .Select(s => s.Symbol)
            .ToList();

        _logger.LogInformation(
            "Symbol probe completed: {Total} symbols, {Success} success, {Transient} transient, {Unknown} unknown. Flagged: {Flagged}",
            symbols.Count, totalSuccess, totalTransient, totalUnknown, flaggedSymbols.Count);

        var jobRuns = _database.GetCollection<BsonDocument>("job_runs");
        var jobRunDoc = new BsonDocument
        {
            ["job_type"] = "SYMBOL_PROBE",
            ["trigger_source"] = string.IsNullOrWhiteSpace(actorId) ? "scheduled" : "manual",
            ["triggered_by"] = triggeredBy,
            ["scheduled_run_time"] = now,
            ["started_at"] = now,
            ["ended_at"] = DateTime.UtcNow,
            ["outcome"] = totalUnknown > 0 ? "partial" : "success",
            ["symbols_processed"] = symbols.Count,
            ["results"] = new BsonDocument
            {
                ["success"] = totalSuccess,
                ["transient"] = totalTransient,
                ["unknown"] = totalUnknown
            },
            ["flagged_symbols"] = BsonArray.Create(flaggedSymbols),
            ["flag_threshold"] = flagThreshold,
            ["errors"] = new BsonArray()
        };

        if (!string.IsNullOrWhiteSpace(actorId))
            jobRunDoc["created_by"] = actorId;

        await jobRuns.InsertOneAsync(jobRunDoc, cancellationToken: ct);

        return new ProbeRunSummary(
            symbols.Count,
            totalSuccess,
            totalTransient,
            totalUnknown,
            jobRunDoc["_id"].AsObjectId.ToString());
    }
}

/// <summary>Result summary returned by <see cref="SymbolProbeService.RunProbeAsync"/>.</summary>
public sealed record ProbeRunSummary(
    int TotalSymbols,
    int SuccessCount,
    int TransientCount,
    int UnknownCount,
    string? JobRunId);
