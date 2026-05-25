using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;
using SignalStack.Signals.Backtesting;
using SignalStack.Storage.Backtesting;
using SignalStack.Domain.Backtesting;
using SignalStack.Historical;
using SignalStack.Storage.Historical;
using SignalStack.Domain.Signals;
using SignalStack.Storage.Signals;
using SignalStack.Storage.Universe;
using SignalStack.Worker.Jobs.EodSuccessMarker;
using SignalStack.Worker.Rme;

namespace SignalStack.Worker.Jobs.EodSignalRunner;

/// <summary>
/// Core service for the EOD Signal Runner (EODSR) job.
///
/// Single-pass per-symbol read: all OHLCV data for the active universe is
/// fetched once and held in memory for the duration of the run. Each user's
/// Signal Subscriptions are evaluated against this shared cache.
///
/// REQ-STRAT-013:  read SQL Server data, evaluate for next-trading-day entry.
/// REQ-STRAT-013a: user-scoped execution with single-pass market data read.
/// REQ-STRAT-027:  atomic-run semantics with db-retry + failed_for_review.
/// REQ-STRAT-028:  provenance fields on every entry signal record.
/// </summary>
public sealed class EodSignalRunnerService
{
    private const string EodsrJobType = "EODSR";
    private const string OutcomeCompleted = "completed";
    private const string OutcomeFailedForReview = "failed_for_review";

    private readonly EodSequencingGate _sequencingGate;
    private readonly ISymbolMasterRepository _symbolMasterRepo;
    private readonly IOhlcvRepository _ohlcvRepo;
    private readonly ISignalSubscriptionRepository _subscriptionRepo;
    private readonly ISignalEvaluatorRegistry _evaluatorRegistry;
    private readonly ITradingCalendarRepository _calendarRepo;
    private readonly IMongoDatabase _database;
    private readonly IOptions<EodSignalRunnerOptions> _options;
    private readonly ILogger<EodSignalRunnerService> _logger;
    private readonly IPositionChannelRegistry _channelRegistry;
    private readonly IPositionRepository _positionRepository;

    public EodSignalRunnerService(
        EodSequencingGate sequencingGate,
        ISymbolMasterRepository symbolMasterRepo,
        IOhlcvRepository ohlcvRepo,
        ISignalSubscriptionRepository subscriptionRepo,
        ISignalEvaluatorRegistry evaluatorRegistry,
        ITradingCalendarRepository calendarRepo,
        IMongoDatabase database,
        IOptions<EodSignalRunnerOptions> options,
        ILogger<EodSignalRunnerService> logger,
        IPositionChannelRegistry channelRegistry,
        IPositionRepository positionRepository)
    {
        _sequencingGate = sequencingGate;
        _symbolMasterRepo = symbolMasterRepo;
        _ohlcvRepo = ohlcvRepo;
        _subscriptionRepo = subscriptionRepo;
        _evaluatorRegistry = evaluatorRegistry;
        _calendarRepo = calendarRepo;
        _database = database;
        _options = options;
        _logger = logger;
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
    }

    /// <summary>
    /// Runs a single EODSR evaluation cycle for the specified trading session.
    /// Returns the outcome: "completed" or "failed_for_review".
    /// </summary>
    public async Task<EodSignalRunnerResult> RunAsync(
        DateOnly sessionDate, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // ── Step 1: Verify DS completion for the target session ─────────
            // REQ-MARKET-007: EODSR refuses to start without DS success marker.
            await _sequencingGate.EnsureDataSyncCompletedAsync(sessionDate, ct);

            // ── Step 2: Determine the next trading day ──────────────────────
            // REQ-STRAT-013: evaluate for next-trading-day entry signals.
            var nextTradingDay = await ResolveNextTradingDayAsync(sessionDate, ct);
            if (nextTradingDay is null)
            {
                _logger.LogWarning(
                    "EODSR: no next trading day found after session {Session}. " +
                    "Skipping run.",
                    sessionDate);
                return new EodSignalRunnerResult(OutcomeCompleted, 0, 0, null);
            }

            _logger.LogInformation(
                "EODSR: evaluating signals for next trading day {NextDay} " +
                "(DS completed for {Session}).",
                nextTradingDay, sessionDate);

            // ── Step 3: Read active universe (single-pass symbol list) ──────
            // Exclude archived symbols (REQ-UNIV-015) and scan_excluded (REQ-STRAT-014).
            var activeSymbols = await _symbolMasterRepo.GetAllAsync(
                archived: false, ct: ct);

            var targetSymbols = activeSymbols
                .Where(s => !s.ScanExcluded)
                .ToList();

            _logger.LogInformation(
                "EODSR: {Total} active symbols in universe ({Excluded} scan-excluded).",
                targetSymbols.Count, activeSymbols.Count - targetSymbols.Count);

            if (targetSymbols.Count == 0)
            {
                _logger.LogWarning("EODSR: no symbols to evaluate. Skipping run.");
                return new EodSignalRunnerResult(OutcomeCompleted, 0, 0, nextTradingDay.Value);
            }

            // ── Step 4: Single-pass OHLCV read ─────────────────────────────
            // REQ-STRAT-013a: read all candle data once, hold in memory.
            var maxCandles = _options.Value.MaxCandlesPerSymbol;
            var candleCache = await ReadAllSymbolDataAsync(
                targetSymbols, nextTradingDay.Value, maxCandles, ct);

            _logger.LogInformation(
                "EODSR: OHLCV data cached for {Count} symbols " +
                "({Failed} symbols had read errors, excluded from evaluation).",
                candleCache.Count,
                targetSymbols.Count - candleCache.Count);

            if (candleCache.Count == 0)
            {
                _logger.LogError(
                    "EODSR: no OHLCV data could be read for any symbol. " +
                    "Marking run as failed_for_review (REQ-STRAT-027).");
                return new EodSignalRunnerResult(OutcomeFailedForReview, 0, 0, nextTradingDay.Value);
            }

            // ── Step 5: Read all active Signal Subscriptions ────────────────
            // REQ-STRAT-007b: reads state once at job start.
            var allSubscriptions = await _subscriptionRepo.GetAllActiveAsync(ct);

            _logger.LogInformation(
                "EODSR: {Count} active subscriptions across {Users} users.",
                allSubscriptions.Count,
                allSubscriptions.Select(s => s.UserId).Distinct().Count());

            if (allSubscriptions.Count == 0)
            {
                _logger.LogInformation("EODSR: no active subscriptions. Completed.");
                return new EodSignalRunnerResult(OutcomeCompleted, 0, 0, nextTradingDay.Value);
            }

            // ── Step 6: Group subscriptions by user and evaluate ────────────
            // REQ-STRAT-013a: user-scoped signal generation.
            // REQ-STRAT-015a: combined signal per (user, symbol, session).
            var now = DateTime.UtcNow;
            var entrySignals = new List<EntrySignalDocument>();

            var userGroups = allSubscriptions
                .GroupBy(s => s.UserId)
                .ToList();

            foreach (var userGroup in userGroups)
            {
                ct.ThrowIfCancellationRequested();

                var userId = userGroup.Key;

                // Activate any pending versions for this user's subscriptions
                // before evaluating (REQ-STRAT-017b).
                foreach (var sub in userGroup)
                {
                    if (sub.PendingVersionIndex.HasValue
                        && sub.PendingVersionIndex.Value < sub.Versions.Count)
                    {
                        var pendingVersion = sub.Versions[sub.PendingVersionIndex.Value];
                        if (pendingVersion.EffectiveFrom <= now)
                        {
                            await _subscriptionRepo.ActivatePendingVersionAsync(
                                sub.Id, sub.PendingVersionIndex.Value, now, ct);
                        }
                    }
                }

                // Build per-subscription state with the now-current version.
                var userSubs = await _subscriptionRepo.GetByUserIdAsync(userId, ct);
                var activeUserSubs = userSubs
                    .Where(s => s.Status == SubscriptionStatus.Active)
                    .ToList();

                // Evaluate each symbol against all user subscriptions.
                // Key: symbol -> list of qualifying subscription evaluations.
                var symbolSignals = new Dictionary<string, List<SubscriptionSignal>>(
                    StringComparer.Ordinal);

                foreach (var sub in activeUserSubs)
                {
                    var evaluator = _evaluatorRegistry.GetEvaluator(sub.SignalTypeId);
                    if (evaluator is null)
                    {
                        _logger.LogWarning(
                            "EODSR: no evaluator registered for Signal type {SignalType} " +
                            "(subscription {SubId}). Skipping.",
                            sub.SignalTypeId, sub.Id);
                        continue;
                    }

                    var parametersJson = sub.Parameters?.ToString();
                    var currentVersion = sub.CurrentVersionIndex >= 0
                        && sub.CurrentVersionIndex < sub.Versions.Count
                        ? sub.Versions[sub.CurrentVersionIndex]
                        : null;

                    foreach (var symbol in targetSymbols)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!candleCache.TryGetValue(symbol.Symbol, out var candles))
                            continue;

                        // Determine timeframe and get appropriate candles.
                        var timeframe = string.IsNullOrWhiteSpace(sub.Timeframe)
                            ? "daily"
                            : sub.Timeframe;

                        var evalCandles = timeframe switch
                        {
                            "daily" => candles.Daily,
                            "weekly" => candles.Weekly,
                            "monthly" => candles.Monthly,
                            "rolling3" => candles.Rolling3,
                            "rolling5" => candles.Rolling5,
                            "rolling7" => candles.Rolling7,
                            _ => candles.Daily
                        };

                        if (evalCandles.Count < 2)
                            continue;

                        var results = evaluator.Evaluate(evalCandles, parametersJson);

                        var entryResults = results
                            .Where(r => r.Action == SignalAction.Entry)
                            .ToList();

                        foreach (var entry in entryResults)
                        {
                            if (!symbolSignals.ContainsKey(symbol.Symbol))
                                symbolSignals[symbol.Symbol] = [];

                            symbolSignals[symbol.Symbol].Add(new SubscriptionSignal
                            {
                                SubscriptionId = sub.Id.ToString(),
                                VersionId = currentVersion?.VersionId.ToString() ?? "",
                                SignalTypeId = sub.SignalTypeId,
                                SignalTypeName = sub.Name,
                                EntryPrice = entry.EntryPrice ?? 0,
                                StopPrice = entry.StopPrice ?? 0,
                                Description = entry.Description ?? "",
                                SignalData = SerializeSignalData(entry)
                            });
                        }
                    }
                }

                // Generate combined entry signals per REQ-STRAT-015a.
                foreach (var (symbol, signals) in symbolSignals)
                {
                    // Combined signal: merge qualifying signal types.
                    var signalTypeIds = signals
                        .Select(s => s.SignalTypeId)
                        .Distinct()
                        .ToList();

                    // For combined signals, use the subscription with highest
                    // priority (by subscription order) as primary per REQ-STRAT-015b.
                    var primarySignal = signals[0];

                    var versionIds = new Dictionary<string, string>();
                    var codeVersionMap = new Dictionary<string, string>();

                    foreach (var sig in signals)
                    {
                        versionIds[sig.SubscriptionId] = sig.VersionId;
                        if (!codeVersionMap.ContainsKey(sig.SignalTypeId))
                            codeVersionMap[sig.SignalTypeId] = sig.CodeVersion;
                    }

                    var signalTypeNames = signals
                        .Select(s => s.SignalTypeName)
                        .Distinct()
                        .ToList();

                    var entrySignal = new EntrySignalDocument
                    {
                        UserId = userId,
                        Symbol = symbol,
                        SessionDate = nextTradingDay.Value.ToString("yyyy-MM-dd"),
                        SignalTypeIds = signalTypeIds,
                        SignalTypeNames = signalTypeNames,
                        SignalSubscriptionVersionIds = versionIds,
                        SignalCodeVersions = codeVersionMap,
                        PrimarySubscriptionId = primarySignal.SubscriptionId,
                        JobRunId = "",  // Set by caller when job_runs entry is known
                        GeneratedAt = now,
                        SignalData = primarySignal.SignalData
                    };

                    entrySignals.Add(entrySignal);
                }
            }

            // ── Step 7: Persist entry signals ──────────────────────────────
            var entrySignalsCollection = _database.GetCollection<EntrySignalDocument>("entry_signals");

            if (entrySignals.Count > 0)
            {
                var signalList = entrySignals.ToList();
                await entrySignalsCollection.InsertManyAsync(signalList, cancellationToken: ct);

                _logger.LogInformation(
                    "EODSR: persisted {Count} entry signals for session {Session} -> next day {NextDay}.",
                    signalList.Count, sessionDate, nextTradingDay);

                // ── Step 7a: Create PendingEntry positions + open channels ──────
                // For each (user, symbol) with a new entry signal, ensure a
                // PendingEntry position exists and open a channel for it.
                // REQ-RME-CONC-005: EODSR wired as event producer through the
                // per-position channel registry.
                await EnsurePositionsForEntrySignalsAsync(signalList, ct);
            }
            else
            {
                _logger.LogInformation(
                    "EODSR: no entry signals generated for session {Session}.",
                    sessionDate);
            }

            sw.Stop();
            _logger.LogInformation(
                "EODSR run for session {Session} completed in {ElapsedMs}ms. " +
                "{SignalCount} entry signals, {SymbolCount} symbols evaluated, " +
                "{UserCount} users.",
                sessionDate, sw.ElapsedMilliseconds,
                entrySignals.Count, candleCache.Count, userGroups.Count);

            return new EodSignalRunnerResult(
                OutcomeCompleted,
                entrySignals.Count,
                candleCache.Count,
                nextTradingDay.Value);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("EODSR run cancelled for session {Session}.", sessionDate);
            return new EodSignalRunnerResult("cancelled", 0, 0, null);
        }
        catch (DataSyncNotCompletedException)
        {
            _logger.LogWarning(
                "EODSR: DataSync has not completed for session {Session}. Skipping run.",
                sessionDate);
            return new EodSignalRunnerResult("skipped_ds_not_ready", 0, 0, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EODSR run failed for session {Session}: {Message}",
                sessionDate, ex.Message);
            return new EodSignalRunnerResult(OutcomeFailedForReview, 0, 0, null);
        }
    }

    /// <summary>
    /// For each distinct (user, symbol) in the entry signal list, creates a
    /// PendingEntry position document if one does not already exist, and opens
    /// a per-position channel so the registry can route future events (e.g.
    /// FillConfirmedEvent from LADS) to the position consumer.
    /// REQ-RME-CONC-005: EODSR wired as event producer.
    /// </summary>
    private async Task EnsurePositionsForEntrySignalsAsync(
        List<EntrySignalDocument> signalList, CancellationToken ct)
    {
        // Group by (user, symbol) — there may be multiple signal types per pair
        var uniquePairs = signalList
            .GroupBy(s => (s.UserId, s.Symbol))
            .ToList();

        foreach (var pair in uniquePairs)
        {
            var (userId, symbol) = pair.Key;

            try
            {
                // Check if a PendingEntry position already exists for this user/symbol
                var existing = await _positionRepository.GetByUserIdAndStateAsync(
                    userId, PositionState.PendingEntry, ct);

                var existingPosition = existing.FirstOrDefault(p =>
                    string.Equals(p.Symbol, symbol, StringComparison.Ordinal));

                if (existingPosition is not null)
                {
                    // Position already exists — ensure channel is open
                    _channelRegistry.OpenChannel(existingPosition.PositionId);
                    continue;
                }

                // Extract entry price from the primary signal's signal_data
                var primarySignal = pair.First();
                var entryPrice = ExtractEntryPrice(primarySignal.SignalData);

                // Create a new PendingEntry position
                var positionId = Guid.NewGuid();
                var position = new PositionDocument
                {
                    PositionId = positionId,
                    UserId = userId,
                    Symbol = symbol,
                    State = PositionState.PendingEntry,
                    EntryPrice = entryPrice,
                    CurrentPrice = entryPrice,
                    Quantity = 0m,          // not yet filled
                    StopLoss = 0m,
                    TrailingStop = 0m,
                    Version = 1,
                    LedgerFenceToken = 0,
                };

                await _positionRepository.CreateAsync(position, ct);
                _channelRegistry.OpenChannel(positionId);

                _logger.LogInformation(
                    "EODSR: created PendingEntry position {PositionId} for user {UserId}, symbol {Symbol} " +
                    "(entryPrice: {Price}). Channel opened per REQ-RME-CONC-005.",
                    positionId, userId, symbol, entryPrice);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "EODSR: failed to create position for user {UserId}, symbol {Symbol}. " +
                    "Entry signal was persisted; position creation will be retried.",
                    userId, symbol);
            }
        }
    }

    /// <summary>
    /// Extracts the entry price from an entry signal's signal_data BSON document.
    /// Falls back to 0 if the data is not available.
    /// </summary>
    private static decimal ExtractEntryPrice(BsonDocument? signalData)
    {
        if (signalData is null) return 0m;

        if (signalData.TryGetValue("entry_price", out var priceValue) && !priceValue.IsBsonNull)
        {
            return priceValue.IsDouble ? (decimal)priceValue.AsDouble
                 : priceValue.IsInt32 ? priceValue.AsInt32
                 : priceValue.IsInt64 ? priceValue.AsInt64
                 : 0m;
        }

        return 0m;
    }

    /// <summary>
    /// Single-pass read: fetch OHLCV data for all symbols and cache in memory.
    /// Implements retry with exponential backoff per REQ-STRAT-027.
    /// </summary>
    private async Task<Dictionary<string, SymbolCandleCache>> ReadAllSymbolDataAsync(
        List<SymbolMasterDocument> symbols,
        DateOnly nextTradingDay,
        int maxCandles,
        CancellationToken ct)
    {
        var cache = new Dictionary<string, SymbolCandleCache>(symbols.Count);
        var retryMaxSeconds = 120; // matches eodsr.db_retry.max_seconds default
        var failedSymbols = 0;

        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();

            var (success, candleData) = await ReadWithRetryAsync(
                symbol.Symbol, nextTradingDay, maxCandles, retryMaxSeconds, ct);

            if (success && candleData is not null)
            {
                cache[symbol.Symbol] = candleData;
            }
            else
            {
                failedSymbols++;
                _logger.LogWarning(
                    "EODSR: failed to read OHLCV data for symbol {Symbol} " +
                    "after retries. Excluding from evaluation.",
                    symbol.Symbol);
            }
        }

        return cache;
    }

    /// <summary>
    /// Reads OHLCV data for a single symbol with exponential backoff retry.
    /// REQ-STRAT-027(a): retry transient SQL Server read errors with exponential
    /// backoff up to eodsr.db_retry.max_seconds.
    /// </summary>
    private async Task<(bool Success, SymbolCandleCache? Data)> ReadWithRetryAsync(
        string symbol,
        DateOnly nextTradingDay,
        int maxCandles,
        int retryMaxSeconds,
        CancellationToken ct)
    {
        var fromDate = nextTradingDay.AddDays(-maxCandles);
        var attempt = 0;
        var delayMs = 200; // initial backoff
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed.TotalSeconds < retryMaxSeconds)
        {
            attempt++;
            try
            {
                var daily = await _ohlcvRepo.GetDailyAsync(
                    symbol, fromDate, nextTradingDay, ct);

                if (daily.Count == 0)
                    return (true, null); // no data available, skip

                // Fetch other timeframes if needed (lazy: weekly, monthly, rolling).
                var weekly = await _ohlcvRepo.GetWeeklyAsync(
                    symbol, fromDate, nextTradingDay, ct);
                var monthly = await _ohlcvRepo.GetMonthlyAsync(
                    symbol, fromDate, nextTradingDay, ct);
                var rolling3 = await _ohlcvRepo.GetRollingAsync(
                    symbol, 3, nextTradingDay, ct);
                var rolling5 = await _ohlcvRepo.GetRollingAsync(
                    symbol, 5, nextTradingDay, ct);
                var rolling7 = await _ohlcvRepo.GetRollingAsync(
                    symbol, 7, nextTradingDay, ct);

                return (true, new SymbolCandleCache(daily, weekly, monthly, rolling3, rolling5, rolling7));
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                _logger.LogWarning(
                    "EODSR: transient read error for symbol {Symbol} " +
                    "(attempt {Attempt}, elapsed {Elapsed:F1}s): {Message}",
                    symbol, attempt, sw.Elapsed.TotalSeconds, ex.Message);

                if (sw.Elapsed.TotalSeconds + (delayMs / 1000.0) >= retryMaxSeconds)
                    break;

                await Task.Delay(delayMs, ct);
                delayMs = Math.Min(delayMs * 2, 10_000); // exponential backoff, max 10s
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Resolves the next trading day after the given session date.
    /// Returns null if no next trading day is found (end of calendar).
    /// </summary>
    private async Task<DateOnly?> ResolveNextTradingDayAsync(
        DateOnly sessionDate, CancellationToken ct)
    {
        // Look ahead up to 14 days for the next trading session.
        var fromDate = sessionDate.AddDays(1);
        var toDate = sessionDate.AddDays(14);

        var sessions = await _calendarRepo.GetSessionsAsync(
            fromDate: fromDate.ToString("yyyy-MM-dd"),
            toDate: toDate.ToString("yyyy-MM-dd"),
            ct: ct);

        var nextSession = sessions
            .Select(s =>
            {
                if (DateOnly.TryParse(s.SessionDate, out var d))
                    return d;
                return (DateOnly?)null;
            })
            .Where(d => d.HasValue && d.Value >= fromDate)
            .OrderBy(d => d!.Value)
            .FirstOrDefault();

        if (nextSession.HasValue)
            return nextSession.Value;

        // Fallback: if calendar is empty, use the next calendar day as a heuristic.
        // This allows EODSR to function before the calendar is fully populated.
        _logger.LogWarning(
            "EODSR: no trading session found in calendar after {Session} " +
            "(looked ahead 14 days). Falling back to next calendar day {Fallback}.",
            sessionDate, fromDate);

        return fromDate;
    }

    private static bool IsTransient(Exception ex)
        => ex is System.Data.Common.DbException
            || ex is TimeoutException;

    /// <summary>
    /// Serialises a <see cref="SignalResult"/> into a <see cref="BsonDocument"/>
    /// for the entry signal record.
    /// </summary>
    private static BsonDocument? SerializeSignalData(SignalResult result)
    {
        if (result.Action != SignalAction.Entry)
            return null;

        var doc = new BsonDocument
        {
            ["entry_price"] = result.EntryPrice.HasValue
                ? BsonValue.Create((double)result.EntryPrice.Value)
                : BsonNull.Value,
            ["stop_price"] = result.StopPrice.HasValue
                ? BsonValue.Create((double)result.StopPrice.Value)
                : BsonNull.Value,
            ["atr"] = result.Atr.HasValue
                ? BsonValue.Create((double)result.Atr.Value)
                : BsonNull.Value,
            ["description"] = result.Description ?? ""
        };

        return doc;
    }
}

/// <summary>Result of a single EODSR run.</summary>
public sealed record EodSignalRunnerResult(
    string Outcome,
    int EntrySignalCount,
    int SymbolsEvaluated,
    DateOnly? NextTradingDay);

/// <summary>
/// In-memory cache of OHLCV data for a single symbol across all timeframes.
/// REQ-STRAT-013a: single-pass — data fetched once, held in memory for evaluation.
/// </summary>
internal sealed record SymbolCandleCache(
    IReadOnlyList<OhlcvRecord> Daily,
    IReadOnlyList<OhlcvRecord> Weekly,
    IReadOnlyList<OhlcvRecord> Monthly,
    IReadOnlyList<OhlcvRecord> Rolling3,
    IReadOnlyList<OhlcvRecord> Rolling5,
    IReadOnlyList<OhlcvRecord> Rolling7);

/// <summary>Internal signal qualifier used during evaluation merging.</summary>
internal sealed class SubscriptionSignal
{
    public string SubscriptionId { get; init; } = "";
    public string VersionId { get; init; } = "";
    public string SignalTypeId { get; init; } = "";
    public string SignalTypeName { get; init; } = "";
    public decimal EntryPrice { get; init; }
    public decimal StopPrice { get; init; }
    public string Description { get; init; } = "";
    public BsonDocument? SignalData { get; init; }

    public string CodeVersion => SignalTypeId switch
    {
        "ma_crossover" => "1.0.0",
        _ => "0.0.0"
    };
}
