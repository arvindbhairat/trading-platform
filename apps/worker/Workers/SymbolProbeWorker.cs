using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;
using SignalStack.Storage.SysConfig;
using SignalStack.Storage.Universe;

namespace SignalStack.Worker.Workers;

/// <summary>
/// Worker BackgroundService that checks for conditions to run the Symbol Validity
/// Probe each trading day: admin FYERS token must have been issued for the first
/// time today, and no probe run for today must already exist.
/// REQ-UNIV-021/021a/021b.
/// </summary>
internal sealed class SymbolProbeWorker : BackgroundService
{
    private const string ProbeEnabledKey = "operations.universe.symbol_probe_enabled";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SymbolProbeWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMinutes(5);

    /// <summary>Tracks whether a probe has been run today to avoid re-triggering.</summary>
    private DateOnly _lastProbeDate;

    public SymbolProbeWorker(
        IServiceProvider serviceProvider,
        ILogger<SymbolProbeWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SymbolProbeWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryRunProbeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SymbolProbeWorker error during probe check.");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task TryRunProbeAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var configRepo = services.GetRequiredService<ISysConfigRepository>();
        var database = services.GetRequiredService<IMongoDatabase>();
        var calendarRepo = services.GetRequiredService<ITradingCalendarRepository>();

        // 1. Check if probe is enabled.
        var enabledDoc = await configRepo.GetByKeyAsync(ProbeEnabledKey, ct);
        var enabled = enabledDoc?.GetValue("value", true) is BsonValue bv && bv.IsBoolean && bv.AsBoolean;
        if (!enabled)
        {
            _logger.LogTrace("Symbol probe is disabled. Skipping check.");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 2. Check if today is a trading day.
        var sessions = await calendarRepo.GetSessionsAsync(
            fromDate: today.ToString("yyyy-MM-dd"),
            toDate: today.ToString("yyyy-MM-dd"),
            ct: ct);

        if (sessions.Count == 0)
        {
            _logger.LogTrace("Today {Date} is not a trading day. Skipping probe.", today);
            return;
        }

        // 3. Check if we've already run the probe today.
        if (_lastProbeDate == today)
        {
            _logger.LogTrace("Probe already run today ({Date}). Skipping.", today);
            return;
        }

        // 4. Check if the admin token was issued today.
        // The admin token must have been successfully generated for the first time today.
        var adminTokenFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("user_id", "admin"),
            Builders<BsonDocument>.Filter.Eq("status", "active"),
            Builders<BsonDocument>.Filter.Gte("issued_at", today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));

        var fyersTokens = database.GetCollection<BsonDocument>("fyers_tokens");
        var adminToken = await fyersTokens.Find(adminTokenFilter).FirstOrDefaultAsync(ct);

        if (adminToken is null)
        {
            _logger.LogTrace("No admin token issued today ({Date}). Skipping probe.", today);
            return;
        }

        // 5. Check that no SYMBOL_PROBE run for today triggered by fyers_token_refresh exists.
        var jobRuns = database.GetCollection<BsonDocument>("job_runs");
        var existingProbeFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("job_type", "SYMBOL_PROBE"),
            Builders<BsonDocument>.Filter.Eq("triggered_by", "fyers_token_refresh"),
            Builders<BsonDocument>.Filter.Gte("started_at", today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));

        var existingRun = await jobRuns.Find(existingProbeFilter).FirstOrDefaultAsync(ct);
        if (existingRun is not null)
        {
            _logger.LogInformation(
                "Probe already run today ({Date}) via fyers_token_refresh. Job run: {JobRunId}.",
                today, existingRun["_id"].AsObjectId.ToString());
            _lastProbeDate = today;
            return;
        }

        // 6. All conditions met — run the probe.
        _logger.LogInformation(
            "Admin token detected for today ({Date}). Triggering symbol validity probe.",
            today);

        var probeService = services.GetRequiredService<SymbolProbeService>();
        var summary = await probeService.RunProbeAsync("fyers_token_refresh", actorId: null, ct);

        if (summary is not null)
        {
            _lastProbeDate = today;
            _logger.LogInformation(
                "Symbol validity probe completed: {Total} symbols, {Success} success, {Transient} transient, {Unknown} unknown.",
                summary.TotalSymbols, summary.SuccessCount, summary.TransientCount, summary.UnknownCount);
        }
    }
}
