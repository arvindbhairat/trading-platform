using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Storage.Universe;
using SignalStack.Storage;
using SignalStack.MarketData;
using SignalStack.Worker.Observability;
using SignalStack.Worker.Push;
using SignalStack.Worker.Rme;

namespace SignalStack.Worker.Jobs.LiveMarketScan;

/// <summary>
/// Per-position data extracted from the positions collection for LMDS evaluation.
/// </summary>
internal sealed record PositionScanInfo(
    ObjectId Id,
    Guid PositionId,
    string UserId,
    string Symbol,
    decimal? CurrentStopLoss,
    decimal? TrailingStopLevel,
    decimal[] AddLevels,
    decimal[] ReduceLevels);

/// <summary>
/// Result of a single LMDS scan cycle.
/// </summary>
public sealed record ScanCycleResult(
    int PositionsEvaluated,
    int SymbolsQuoted,
    int BreachesDetected,
    int StaleQuoteCount,
    TimeSpan Elapsed,
    bool ExceededCapacityThreshold);

/// <summary>
/// Core service for the Live Market Data Scan (LMDS) job.
///
/// During NSE market hours, fetches live quotes for all symbols with open
/// positions across all users, evaluates current price against each position's
/// active stop, add, and reduce levels, and enqueues
/// <see cref="PriceLevelBreachedEvent"/> records through the
/// <see cref="IPositionChannelRegistry"/> when levels are breached.
///
/// REQ-STOP-006:   continuous intraday price + level monitoring.
/// REQ-STOP-006a:  market-hours only via calendar check in worker.
/// REQ-STOP-006b:  per-position level evaluation (stop, add, reduce, trailing stop).
/// REQ-STOP-006c:  quote-delay indicator on each quote processed.
/// REQ-SLO-004:    cycle must complete within 70 % of configured poll interval.
/// REQ-SLO-011:    capacity warning at <c>jobs.lmds.capacity_warn_pct</c>.
/// REQ-PLC-004:    circuit-clear re-evaluation against first post-clear tick.
/// </summary>
public sealed class LiveMarketScanService
{
    private readonly IMongoDatabase _database;
    private readonly ISymbolMasterRepository _symbolMasterRepo;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IPositionChannelRegistry _channelRegistry;
    private readonly IPushEventPublisher _pushEventPublisher;
    private readonly IOptions<LiveMarketScanOptions> _options;
    private readonly ILogger<LiveMarketScanService> _logger;

    // ── OTEL metrics ─────────────────────────────────────────────────────
    private static readonly Counter<long> ScanCyclesTotal = WorkerTelemetry.Meter
        .CreateCounter<long>("lmds.scan.cycles.total",
            description: "Incremented per LMDS scan cycle completed.");
    private static readonly Counter<long> BreachesDetectedTotal = WorkerTelemetry.Meter
        .CreateCounter<long>("lmds.breaches.detected.total",
            description: "Total level breaches detected across all LMDS cycles.");
    private static readonly Histogram<double> ScanCycleDurationMs = WorkerTelemetry.Meter
        .CreateHistogram<double>("lmds.scan.cycle.duration_ms",
            description: "Duration of each LMDS scan cycle in milliseconds.");
    private static readonly Histogram<long> PositionsScannedPerCycle = WorkerTelemetry.Meter
        .CreateHistogram<long>("lmds.positions.scanned_per_cycle",
            description: "Number of positions evaluated per LMDS scan cycle.");

    // ── Circuit-clear state (REQ-PLC-004) ─────────────────────────────────
    private int _consecutiveStaleCycles;
    public LiveMarketScanService(
        IMongoDatabase database,
        ISymbolMasterRepository symbolMasterRepo,
        IMarketDataProvider marketDataProvider,
        IPositionChannelRegistry channelRegistry,
        IPushEventPublisher pushEventPublisher,
        IOptions<LiveMarketScanOptions> options,
        ILogger<LiveMarketScanService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _symbolMasterRepo = symbolMasterRepo ?? throw new ArgumentNullException(nameof(symbolMasterRepo));
        _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _pushEventPublisher = pushEventPublisher ?? throw new ArgumentNullException(nameof(pushEventPublisher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a single LMDS scan cycle.
    /// </summary>
    public async Task<ScanCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // ── Step 1: Load non-terminal positions ──────────────────────────────
        var positions = await LoadActivePositionsAsync(ct);
        if (positions.Count == 0)
        {
            sw.Stop();
            var emptyResult = new ScanCycleResult(0, 0, 0, 0, sw.Elapsed, false);
            RecordMetrics(emptyResult);
            return emptyResult;
        }

        // ── Step 1b: Exclude positions whose symbols are archived or scan-excluded ──
        // REQ-STRAT-014: LMDS must honour admin-configured scan exclusions.
        // REQ-UNIV-015:  archived symbols excluded from scans.
        var excludedSymbols = await GetExcludedSymbolsAsync(ct);
        if (excludedSymbols.Count > 0)
        {
            var beforeCount = positions.Count;
            positions = positions.Where(p => !excludedSymbols.Contains(p.Symbol)).ToList();
            if (positions.Count != beforeCount)
            {
                _logger.LogInformation(
                    "LMDS: excluded {Count} positions whose symbols are archived or scan-excluded.",
                    beforeCount - positions.Count);

                if (positions.Count == 0)
                {
                    sw.Stop();
                    var emptyResult = new ScanCycleResult(0, 0, 0, 0, sw.Elapsed, false);
                    RecordMetrics(emptyResult);
                    return emptyResult;
                }
            }
        }

        // ── Step 2: Collate unique symbols ───────────────────────────────────
        var uniqueSymbols = positions
            .Select(p => p.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(_options.Value.MaxPositionsPerCycle)
            .ToList();

        // ── Step 3: Fetch quotes ────────────────────────────────────────────
        var quotes = await FetchQuotesAsync(uniqueSymbols, ct);

        var staleQuoteCount = quotes.Values.Count(q => !q.IsFresh);
        var positionsEvaluated = 0;
        var breachesDetected = 0;

        // ── Step 4: Per-position level evaluation ────────────────────────────
        foreach (var position in positions.Take(_options.Value.MaxPositionsPerCycle))
        {
            if (ct.IsCancellationRequested)
                break;

            if (!quotes.TryGetValue(position.Symbol, out var quote))
            {
                _logger.LogTrace(
                    "LMDS: no quote available for {Symbol} (position {PositionId}). Skipping.",
                    position.Symbol, position.PositionId);
                continue;
            }

            positionsEvaluated++;
            breachesDetected += EvaluatePositionLevels(position, quote, ct);
        }

        sw.Stop();

        // ── Step 5: Circuit-clear detection (REQ-PLC-004) ────────────────────
        if (positionsEvaluated > 0 && staleQuoteCount >= positionsEvaluated)
        {
            _consecutiveStaleCycles++;
            _logger.LogWarning(
                "LMDS: all {Count} evaluated positions have stale quotes ({Consecutive} consecutive cycles). " +
                "Possible market stall or circuit condition.",
                positionsEvaluated, _consecutiveStaleCycles);
        }
        else
        {
            // At least one fresh quote — market is responsive.
            if (_consecutiveStaleCycles >= _options.Value.StaleQuoteCircuitThreshold)
            {
                _logger.LogInformation(
                    "LMDS: market resumed after {StallCycles} cycles of stale quotes. " +
                    "Circuit-clear condition detected (REQ-PLC-004).",
                    _consecutiveStaleCycles);
            }

            _consecutiveStaleCycles = 0;
        }

        // ── Step 6: Capacity check (REQ-SLO-004 / REQ-SLO-011) ──────────────
        var pollIntervalMs = _options.Value.PollIntervalSeconds * 1000;
        var capacityThresholdMs = pollIntervalMs * _options.Value.CapacityWarnPct / 100;
        var exceededCapacity = sw.ElapsedMilliseconds > capacityThresholdMs;

        if (exceededCapacity)
        {
            _logger.LogWarning(
                "LMDS: scan cycle took {ElapsedMs} ms " +
                "({CapacityPct}% of {PollInterval}s poll interval). " +
                "REQ-SLO-004 cycle-time threshold exceeded. " +
                "Consider increasing poll interval or reducing position count.",
                sw.ElapsedMilliseconds,
                _options.Value.CapacityWarnPct,
                _options.Value.PollIntervalSeconds);
        }

        var result = new ScanCycleResult(
            positionsEvaluated,
            uniqueSymbols.Count,
            breachesDetected,
            staleQuoteCount,
            sw.Elapsed,
            exceededCapacity);

        RecordMetrics(result);
        return result;
    }

    /// <summary>
    /// Loads all non-terminal position documents from the positions collection.
    /// REQ-STOP-006b: evaluates all open, pending-entry, and suspended positions.
    /// </summary>
    private async Task<List<PositionScanInfo>> LoadActivePositionsAsync(CancellationToken ct)
    {
        var positionsCollection = _database.GetCollection<BsonDocument>("positions");

        var nonTerminalStates = new[] { "PendingEntry", "Open", "Suspended" };
        var filter = Builders<BsonDocument>.Filter.In("state", nonTerminalStates);

        var docs = await positionsCollection
            .Find(filter)
            .Limit(_options.Value.MaxPositionsPerCycle)
            .ToListAsync(ct);

        var result = new List<PositionScanInfo>(docs.Count);

        foreach (var doc in docs)
        {
            if (!TryParsePosition(doc, out var info))
                continue;
            result.Add(info);
        }

        return result;
    }

    /// <summary>
    /// Loads the symbol master and returns a set of symbols that should be excluded
    /// from LMDS scanning: archived symbols (REQ-UNIV-015) and scan-excluded symbols (REQ-STRAT-014).
    /// </summary>
    private async Task<HashSet<string>> GetExcludedSymbolsAsync(CancellationToken ct)
    {
        var allSymbols = await _symbolMasterRepo.GetAllAsync(archived: null, ct);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sym in allSymbols)
        {
            if (sym.IsArchived || sym.ScanExcluded)
                excluded.Add(sym.Symbol);
        }

        return excluded;
    }

    /// <summary>
    /// Attempts to parse a position BSON document into <see cref="PositionScanInfo"/>.
    /// Returns false if required fields (position_id, symbol) are missing.
    /// </summary>
    private static bool TryParsePosition(BsonDocument doc, out PositionScanInfo info)
    {
        info = default!;

        if (!doc.TryGetValue("position_id", out var pid) || pid is not BsonBinaryData binary)
        {
            // Some older documents may store position_id as a string.
            if (doc.TryGetValue("position_id", out var pidStr) && pidStr.IsString
                && Guid.TryParse(pidStr.AsString, out var parsedGuid))
            {
                return BuildPositionScanInfo(doc, parsedGuid, out info);
            }

            return false;
        }

        var positionId = binary.AsGuid;
        return BuildPositionScanInfo(doc, positionId, out info);
    }

    private static bool BuildPositionScanInfo(BsonDocument doc, Guid positionId, out PositionScanInfo info)
    {
        info = default!;

        if (!doc.TryGetValue("symbol", out var symbol) || !symbol.IsString)
            return false;

        // Parse optional level fields — null if not set or zero.
        decimal? stopLoss = null;
        if (doc.TryGetValue("current_stop_loss", out var sl) && !sl.IsBsonNull)
        {
            var slDecimal = sl.ToDecimal();
            if (slDecimal > 0) stopLoss = slDecimal;
        }

        decimal? trailingStop = null;
        if (doc.TryGetValue("trailing_stop_current_level", out var ts) && !ts.IsBsonNull)
        {
            var tsDecimal = ts.ToDecimal();
            if (tsDecimal > 0) trailingStop = tsDecimal;
        }

        var addLevels = ParseDecimalArray(doc, "add_levels");
        var reduceLevels = ParseDecimalArray(doc, "reduce_levels");

        info = new PositionScanInfo(
            Id: doc["_id"].AsObjectId,
            PositionId: positionId,
            UserId: doc.GetBsonStringOrNull("user_id") ?? "",
            Symbol: symbol.AsString,
            CurrentStopLoss: stopLoss,
            TrailingStopLevel: trailingStop,
            AddLevels: addLevels,
            ReduceLevels: reduceLevels);

        return true;
    }

    /// <summary>
    /// Parses a decimal array field from a BSON document.
    /// Returns empty array if field is missing or not an array.
    /// </summary>
    private static decimal[] ParseDecimalArray(BsonDocument doc, string fieldName)
    {
        if (!doc.TryGetValue(fieldName, out var value) || value is not BsonArray arr)
            return [];

        return arr
            .Select(v => v.IsBsonNull ? (decimal?)null : v.ToDecimal())
            .Where(v => v.HasValue && v.Value > 0)
            .Select(v => v!.Value)
            .ToArray();
    }

    /// <summary>
    /// Fetches live quotes for the given symbols via the MDP.
    /// Returns a dictionary keyed by symbol (uppercase).
    /// </summary>
    private async Task<Dictionary<string, LiveQuote>> FetchQuotesAsync(
        List<string> symbols, CancellationToken ct)
    {
        var result = new Dictionary<string, LiveQuote>(symbols.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var quote = await _marketDataProvider.GetLatestQuoteAsync(symbol, ct);
                if (quote is null)
                {
                    _logger.LogTrace("LMDS: no quote returned for {Symbol}.", symbol);
                    continue;
                }

                result[symbol] = new LiveQuote(
                    Price: quote.LastPrice,
                    TimestampUtc: quote.TimestampUtc,
                    IsFresh: !quote.IsDelayed
                        && (DateTime.UtcNow - quote.TimestampUtc).TotalSeconds
                           <= StaleAfterSecondsDefault);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LMDS: error fetching quote for {Symbol}.", symbol);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                Activity.Current?.AddException(ex);
            }
        }

        return result;
    }

    /// <summary>
    /// Evaluates all monitored levels for a single position against the current
    /// quote price. Enqueues <see cref="PriceLevelBreachedEvent"/> for each
    /// breached level. Returns the number of breaches detected.
    /// </summary>
    private int EvaluatePositionLevels(
        PositionScanInfo position, LiveQuote quote, CancellationToken ct)
    {
        var breaches = 0;
        var price = quote.Price;
        var now = DateTimeOffset.UtcNow;

        // ── Stop level (breach when price <= stop) ──────────────────────────
        if (position.CurrentStopLoss.HasValue && price <= position.CurrentStopLoss.Value)
        {
            EnqueueBreach(position, LevelType.Stop, price, now, ct);
            breaches++;
        }

        // ── Trailing stop level (breach when price <= trailing level) ───────
        if (position.TrailingStopLevel.HasValue && price <= position.TrailingStopLevel.Value)
        {
            EnqueueBreach(position, LevelType.TrailingStop, price, now, ct);
            breaches++;
        }

        // ── Add levels (breach when price >= level) ─────────────────────────
        foreach (var level in position.AddLevels)
        {
            if (price >= level)
            {
                EnqueueBreach(position, LevelType.Add, price, now, ct);
                breaches++;
            }
        }

        // ── Reduce levels (breach when price >= level) ──────────────────────
        foreach (var level in position.ReduceLevels)
        {
            if (price >= level)
            {
                EnqueueBreach(position, LevelType.Reduce, price, now, ct);
                breaches++;
            }
        }

        return breaches;
    }

    /// <summary>
    /// Enqueues a <see cref="PriceLevelBreachedEvent"/> to the channel registry
    /// and publishes a push event for real-time delivery to connected portal clients.
    /// REQ-STOP-006b: stop, add, reduce, trailing-stop levels evaluated.
    /// REQ-NFR-013: push events delivered via WebSocket fan-out.
    /// </summary>
    private void EnqueueBreach(
        PositionScanInfo position, LevelType levelType, decimal price,
        DateTimeOffset occurredAt, CancellationToken ct)
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = position.PositionId,
            OccurredAt = occurredAt,
            Source = "LMDS",
            Level = levelType,
            Price = price,
        };

        // Fire-and-forget via ValueTask; log if the channel is full.
        try
        {
            _channelRegistry.EnqueueAsync(evt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LMDS: failed to enqueue {LevelType} breach event for position {PositionId}.",
                levelType, position.PositionId);
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Activity.Current?.AddException(ex);
        }

        // Publish push event for real-time portal delivery (REQ-NFR-013).
        var (pushEventType, levelTypeStr) = levelType switch
        {
            LevelType.Stop => ("stop_breach", "Stop"),
            LevelType.Add => ("add_advisory", "Add"),
            LevelType.Reduce => ("reduce_advisory", "Reduce"),
            LevelType.TrailingStop => ("stop_breach", "TrailingStop"),
            _ => ("notification", levelType.ToString())
        };

        // Fire-and-forget; push publisher handles Redis failure gracefully
        // per REQ-DATA-006a(c)(i).
        _ = PublishPushEventAsync(pushEventType, position, levelTypeStr, price, occurredAt, ct);
    }

    /// <summary>
    /// Publishes a push event to the Redis fan-out channel.
    /// REQ-DATA-006a(c)(i): logs and degrades gracefully on Redis failure.
    /// </summary>
    private async Task PublishPushEventAsync(
        string eventType, PositionScanInfo position, string levelType,
        decimal price, DateTimeOffset occurredAt, CancellationToken ct)
    {
        try
        {
            var content = levelType switch
            {
                "Stop" => $"Stop level breached for {position.Symbol} at {price:F2}",
                "Add" => $"Add advisory triggered for {position.Symbol} at {price:F2}",
                "Reduce" => $"Reduce advisory triggered for {position.Symbol} at {price:F2}",
                "TrailingStop" => $"Trailing stop breached for {position.Symbol} at {price:F2}",
                _ => $"Level {levelType} breached for {position.Symbol} at {price:F2}"
            };

            await _pushEventPublisher.PublishAsync(
                eventType: eventType,
                userId: position.UserId,
                symbol: position.Symbol,
                positionId: position.PositionId.ToString(),
                content: content,
                deepLink: $"/chart?symbol={position.Symbol}",
                price: price,
                levelType: levelType,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LMDS: failed to publish push event for {LevelType} breach on position {PositionId}.",
                levelType, position.PositionId);
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Activity.Current?.AddException(ex);
        }
    }

    /// <summary>
    /// Records OTEL metrics for the completed scan cycle.
    /// </summary>
    private void RecordMetrics(ScanCycleResult result)
    {
        ScanCyclesTotal.Add(1);
        BreachesDetectedTotal.Add(result.BreachesDetected);
        ScanCycleDurationMs.Record(result.Elapsed.TotalMilliseconds);
        PositionsScannedPerCycle.Record(result.PositionsEvaluated);
    }

    /// <summary>
    /// Default staleness threshold for quotes when sys_config is unavailable.
    /// Matches the default for <c>market_data.quote.stale_after_seconds</c>.
    /// </summary>
    private const int StaleAfterSecondsDefault = 300;
}

/// <summary>
/// A live quote for a symbol, with staleness metadata.
/// </summary>
internal sealed record LiveQuote(
    decimal Price,
    DateTime TimestampUtc,
    bool IsFresh);
