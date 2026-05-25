using MongoDB.Bson;
using SignalStack.Domain.Audit;
using SignalStack.Domain.Notifications;
using SignalStack.Storage.Notifications;
using SignalStack.Storage.SysConfig;
using Microsoft.Extensions.Logging;

namespace SignalStack.Signals.Backtesting;

/// <summary>
/// BTE-vs-RME parity check service.
///
/// Runs a fixed set of synthetic scenarios through the same RME configuration
/// parsing that the BacktestEngine uses, and compares the results against
/// independently-computed reference values. Any divergence beyond the configured
/// threshold fails the check and produces an admin notification + audit event.
///
/// REQ-RME-003: identical inputs must produce identical outputs; this service
/// verifies that the RME-aware backtest path and the reference model agree.
/// REQ-NFR-017: the nightly parity check gating Phase 7.
/// </summary>
public sealed class BteRmeParityService
{
    private readonly ISysConfigRepository _sysConfig;
    private readonly IAuditEventRepository _audit;
    private readonly INotificationWriter _notificationWriter;
    private readonly ILogger<BteRmeParityService> _logger;

    // Default threshold — stop/sizing divergence beyond this % fails the check.
    private const decimal DefaultDivergenceThresholdPct = 1.0m;

    public BteRmeParityService(
        ISysConfigRepository sysConfig,
        IAuditEventRepository audit,
        INotificationWriter notificationWriter,
        ILogger<BteRmeParityService> logger)
    {
        _sysConfig = sysConfig;
        _audit = audit;
        _notificationWriter = notificationWriter;
        _logger = logger;
    }

    /// <summary>
    /// Runs the BTE-vs-RME parity check against the built-in synthetic fixture.
    /// </summary>
    public async Task<BteRmeParityResult> RunParityCheckAsync(CancellationToken ct = default)
    {
        var timestamp = DateTime.UtcNow;
        _logger.LogInformation("BTE-vs-RME parity check starting at {Timestamp:O}", timestamp);

        // ── Check toggle ────────────────────────────────────────────────────
        // sys_config stores boolean values as 0/1 long integers.
        var enabledRaw = await _sysConfig.GetLongAsync("operations.bte_rme_parity.enabled", 1, ct);
        var enabled = enabledRaw != 0;
        if (!enabled)
        {
            _logger.LogInformation(
                "BTE-vs-RME parity check skipped: operations.bte_rme_parity.enabled = false");
            return new BteRmeParityResult
            {
                Passed = true,
                CheckTimestamp = timestamp,
                Summary = "Parity check skipped: operations.bte_rme_parity.enabled = false",
            };
        }

        var thresholdPct = await _sysConfig.GetDecimalAsync(
            "risk.parity.divergence_threshold_pct", DefaultDivergenceThresholdPct, ct);

        // ── Build synthetic scenarios ──────────────────────────────────────
        // Each scenario is an (entry_price, atr, expected_stop_pct, expected_size_pct)
        // tuple that exercises a different RME profile config.
        var scenarios = BuildScenarios();
        _logger.LogInformation("Parity check: {Count} scenarios to evaluate", scenarios.Count);

        var divergences = new List<ScenarioDivergenceDetail>();
        var maxStopDivPct = 0m;
        var maxSizeDivPct = 0m;

        foreach (var sc in scenarios)
        {
            // Compute what the shared RME config parser produces
            var (computedStop, computedQuantity) = ComputeRmePositionParameters(
                sc.EntryPrice, sc.Atr, sc.RmeConfigJson);

            // Compute reference values (what the RME model *should* produce)
            var referenceStop = sc.EntryPrice * (1m - sc.ExpectedStopPct / 100m);
            var referenceQuantity = sc.ExpectedQuantity;

            // Stop price divergence
            if (referenceStop > 0)
            {
                var stopDivPct = Math.Abs(computedStop - referenceStop) / referenceStop * 100m;
                maxStopDivPct = Math.Max(maxStopDivPct, stopDivPct);

                if (stopDivPct > thresholdPct)
                {
                    divergences.Add(new ScenarioDivergenceDetail
                    {
                        ScenarioLabel = sc.Label,
                        Metric = "stop_price",
                        ComputedValue = computedStop,
                        ReferenceValue = referenceStop,
                        DivergencePct = Math.Round(stopDivPct, 4),
                    });
                }
            }

            // Position size divergence
            if (referenceQuantity > 0)
            {
                var sizeDivPct = Math.Abs(computedQuantity - referenceQuantity)
                    / (decimal)referenceQuantity * 100m;
                maxSizeDivPct = Math.Max(maxSizeDivPct, sizeDivPct);

                if (sizeDivPct > thresholdPct)
                {
                    divergences.Add(new ScenarioDivergenceDetail
                    {
                        ScenarioLabel = sc.Label,
                        Metric = "position_size",
                        ComputedValue = computedQuantity,
                        ReferenceValue = referenceQuantity,
                        DivergencePct = Math.Round(sizeDivPct, 4),
                    });
                }
            }
        }

        var passed = divergences.Count == 0;
        var summary = passed
            ? $"BTE-vs-RME parity passed: {scenarios.Count} scenarios evaluated, "
              + $"max stop divergence {maxStopDivPct:F2}%, max size divergence {maxSizeDivPct:F2}%"
            : $"BTE-vs-RME parity FAILED: {divergences.Count}/{scenarios.Count} scenarios diverged, "
              + $"max stop divergence {maxStopDivPct:F2}%, threshold {thresholdPct}%";

        var result = new BteRmeParityResult
        {
            Passed = passed,
            CheckTimestamp = timestamp,
            ScenariosEvaluated = scenarios.Count,
            ScenariosWithDivergence = divergences.Count,
            MaxStopPriceDivergencePct = Math.Round(maxStopDivPct, 4),
            MaxSizingDivergencePct = Math.Round(maxSizeDivPct, 4),
            DivergenceThresholdPct = thresholdPct,
            Divergences = divergences.Count > 0 ? divergences : null,
            Summary = summary,
        };

        _logger.LogInformation(
            "BTE-vs-RME parity {Status}: {Summary}",
            passed ? "PASSED" : "FAILED", summary);

        // ── On failure: admin notification + audit event ────────────────────
        if (!passed)
        {
            await RecordParityFailureAsync(result, ct);
        }

        return result;
    }

    // ── Scenario definitions ───────────────────────────────────────────────

    /// <summary>
    /// Builds the fixed synthetic fixture scenarios that exercise various RME
    /// profile configurations. Each scenario has known inputs and expected outputs.
    /// </summary>
    private static List<ParityScenario> BuildScenarios()
    {
        // Base equity for all scenarios: 10M INR
        const decimal equity = 10_000_000m;

        return
        [
            // Scenario 1: Fixed stop at 2%, fixed_percentage sizing at 0.5% risk
            new()
            {
                Label = "fixed_stop_2pct",
                EntryPrice = 1500m,
                Atr = 30m,
                RmeConfigJson = BuildRmeConfigJson("fixed_percentage", 0.5m, "fixed", 2.0m),
                ExpectedStopPct = 2.0m,
                ExpectedQuantity = ComputeExpectedQuantity(equity, 0.5m, 1500m, 1500m * 0.02m),
            },
            // Scenario 2: Fixed stop at 1%, fixed_percentage sizing at 1% risk
            new()
            {
                Label = "fixed_stop_1pct_risk_1pct",
                EntryPrice = 500m,
                Atr = 10m,
                RmeConfigJson = BuildRmeConfigJson("fixed_percentage", 1.0m, "fixed", 1.0m),
                ExpectedStopPct = 1.0m,
                ExpectedQuantity = ComputeExpectedQuantity(equity, 1.0m, 500m, 500m * 0.01m),
            },
            // Scenario 3: ATR stop at 2x multiplier, fixed_percentage sizing at 0.5%
            new()
            {
                Label = "atr_stop_2x_sizing_0.5pct",
                EntryPrice = 2500m,
                Atr = 50m,
                RmeConfigJson = BuildAtrRmeConfigJson("fixed_percentage", 0.5m, 2.0m),
                ExpectedStopPct = 50m * 2.0m / 2500m * 100m, // (ATR * multiplier) / price as %
                ExpectedQuantity = ComputeExpectedQuantity(equity, 0.5m, 2500m, 50m * 2.0m),
            },
            // Scenario 4: ATR stop at 3x multiplier, fixed_percentage sizing at 0.5%
            new()
            {
                Label = "atr_stop_3x_sizing_0.5pct",
                EntryPrice = 800m,
                Atr = 15m,
                RmeConfigJson = BuildAtrRmeConfigJson("fixed_percentage", 0.5m, 3.0m),
                ExpectedStopPct = 15m * 3.0m / 800m * 100m,
                ExpectedQuantity = ComputeExpectedQuantity(equity, 0.5m, 800m, 15m * 3.0m),
            },
            // Scenario 5: Higher volatility, ATR stop at 1.5x
            new()
            {
                Label = "atr_stop_1.5x_high_vol",
                EntryPrice = 120m,
                Atr = 8m,
                RmeConfigJson = BuildAtrRmeConfigJson("fixed_percentage", 0.5m, 1.5m),
                ExpectedStopPct = 8m * 1.5m / 120m * 100m,
                ExpectedQuantity = ComputeExpectedQuantity(equity, 0.5m, 120m, 8m * 1.5m),
            },
        ];
    }

    /// <summary>Builds RME config JSON for a fixed-stop profile.</summary>
    private static string BuildRmeConfigJson(
        string sizingType, decimal riskPct, string stopType, decimal stopPct)
    {
        return $$"""
            {
              "SizingModelType": "{{sizingType}}",
              "RiskPerTradePct": {{riskPct}},
              "StopLossType": "{{stopType}}",
              "StopLossPct": {{stopPct}},
              "Timeframe": "daily"
            }
            """;
    }

    /// <summary>Builds RME config JSON for an ATR-stop profile.</summary>
    private static string BuildAtrRmeConfigJson(
        string sizingType, decimal riskPct, decimal atrMultiplier)
    {
        return $$"""
            {
              "SizingModelType": "{{sizingType}}",
              "RiskPerTradePct": {{riskPct}},
              "StopLossType": "atr",
              "AtrStopMultiplier": {{atrMultiplier}},
              "AtrPeriod": 14,
              "Timeframe": "daily"
            }
            """;
    }

    /// <summary>
    /// Computes the expected quantity for a backtest position given equity,
    /// risk %, entry price, and stop distance (risk per share).
    /// This mirrors the logic that the BacktestEngine should use when
    /// RME configuration is provided (REQ-RME-003 shared implementation).
    /// </summary>
    private static int ComputeExpectedQuantity(
        decimal equity, decimal riskPct, decimal entryPrice, decimal riskPerShare)
    {
        if (riskPerShare <= 0) return 1;
        var riskCapital = equity * (riskPct / 100m);
        var qty = (int)(riskCapital / riskPerShare);
        return Math.Max(1, qty);
    }

    /// <summary>
    /// Delegates to <see cref="RmeConfigParser.ComputePositionParameters"/> —
    /// the same shared parser that the BacktestEngine uses — so the parity
    /// check tests the integration path directly.
    ///
    /// REQ-RME-003: identical inputs → identical outputs regardless of whether
    /// the caller is the backtest engine or the parity check.
    /// </summary>
    internal static (decimal StopPrice, int Quantity) ComputeRmePositionParameters(
        decimal entryPrice,
        decimal? atr,
        string? rmeConfigJson,
        decimal equity = 10_000_000m)
        => RmeConfigParser.ComputePositionParameters(entryPrice, atr, rmeConfigJson, equity);

    // ── Failure recording ──────────────────────────────────────────────────

    private async Task RecordParityFailureAsync(
        BteRmeParityResult result, CancellationToken ct)
    {
        // Audit event
        var details = new Dictionary<string, object?>
        {
            ["scenarios_evaluated"] = result.ScenariosEvaluated,
            ["scenarios_with_divergence"] = result.ScenariosWithDivergence,
            ["max_stop_divergence_pct"] = result.MaxStopPriceDivergencePct,
            ["max_sizing_divergence_pct"] = result.MaxSizingDivergencePct,
            ["threshold_pct"] = result.DivergenceThresholdPct,
            ["check_timestamp"] = result.CheckTimestamp.ToString("O"),
        };

        await _audit.RecordAsync(
            actorId: "system",
            actionType: "bte_rme_parity_failure",
            eventAt: DateTime.UtcNow,
            details: details,
            cancellationToken: ct);

        // Admin notification — ObjectId.Empty broadcasts to all admin users.
        // REQ-NOTIFY-021: set email delivery fields to trigger email dispatch.
        var detailsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            scenarios_evaluated = result.ScenariosEvaluated,
            scenarios_with_divergence = result.ScenariosWithDivergence,
            max_stop_divergence_pct = result.MaxStopPriceDivergencePct,
            max_sizing_divergence_pct = result.MaxSizingDivergencePct,
            threshold_pct = result.DivergenceThresholdPct,
        });

        var notification = new NotificationDocument
        {
            UserId = ObjectId.Empty, // admin broadcast
            NotificationType = NotificationType.AdminBteRmeParityFailure,
            Content = result.Summary + " | Details: " + detailsJson,
            GeneratedAt = DateTime.UtcNow,
            IsAdminNotification = true,
        };
        notification.TelegramDeliveryStatus = DeliveryStatus.Pending;
        notification.EmailDeliveryStatus = "not_attempted";

        await _notificationWriter.WriteAsync(notification, ct);
        _logger.LogWarning(
            "BTE-vs-RME parity failure recorded: admin notification + audit event written. {Summary}",
            result.Summary);
    }

    // ── Internal types ─────────────────────────────────────────────────────

    private sealed record ParityScenario
    {
        public required string Label { get; init; }
        public required decimal EntryPrice { get; init; }
        public required decimal? Atr { get; init; }
        public required string RmeConfigJson { get; init; }
        public required decimal ExpectedStopPct { get; init; }
        public required int ExpectedQuantity { get; init; }
    }
}
