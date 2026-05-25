namespace SignalStack.Signals.Backtesting;

/// <summary>
/// Shared RME configuration parser used by both the BacktestEngine and the
/// BTE-vs-RME parity check. Encodes the stop-loss and position-sizing logic
/// that must be consistent between backtest and live RME workflows.
///
/// REQ-RME-003: identical inputs → identical outputs.
/// REQ-RME-002: same risk logic in backtest and live modes.
/// </summary>
public static class RmeConfigParser
{
    /// <summary>
    /// Computes stop price and position quantity from RME profile configuration.
    /// When <paramref name="rmeConfigJson"/> is null or empty, falls back to
    /// legacy defaults (5% stop, quantity 1).
    /// </summary>
    /// <param name="entryPrice">The entry price for the position.</param>
    /// <param name="atr">Optional ATR value for ATR-based stops/sizing or null.</param>
    /// <param name="rmeConfigJson">RME profile JSON (RmeProfile shape).</param>
    /// <param name="equity">Account equity used for position sizing (default 10M).</param>
    /// <returns>(stopPrice, quantity) tuple.</returns>
    public static (decimal StopPrice, int Quantity) ComputePositionParameters(
        decimal entryPrice,
        decimal? atr,
        string? rmeConfigJson,
        decimal equity = 10_000_000m)
    {
        if (string.IsNullOrWhiteSpace(rmeConfigJson))
        {
            return (entryPrice * 0.95m, 1);
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rmeConfigJson);
            var root = doc.RootElement;

            var stopLossType = root.TryGetProperty("StopLossType", out var slt)
                ? slt.GetString() ?? "fixed"
                : "fixed";

            var sizingType = root.TryGetProperty("SizingModelType", out var smt)
                ? smt.GetString() ?? "fixed_percentage"
                : "fixed_percentage";

            // ── Compute stop price ──────────────────────────────────────────
            var stopPrice = ComputeStopPrice(entryPrice, atr, stopLossType, root);

            // ── Compute position quantity ───────────────────────────────────
            var quantity = ComputeQuantity(entryPrice, stopPrice, sizingType, equity, root);

            return (stopPrice, quantity);
        }
        catch
        {
            return (entryPrice * 0.95m, 1);
        }
    }

    private static decimal ComputeStopPrice(
        decimal entryPrice, decimal? atr, string stopLossType,
        System.Text.Json.JsonElement root)
    {
        switch (stopLossType.ToLowerInvariant())
        {
            case "fixed":
            {
                var stopPct = root.TryGetProperty("StopLossPct", out var slp)
                    ? slp.GetDecimal()
                    : 2.0m;
                return entryPrice * (1m - stopPct / 100m);
            }

            case "atr":
            {
                var atrVal = atr ?? entryPrice * 0.02m;
                var atrMult = root.TryGetProperty("AtrStopMultiplier", out var atm)
                    ? atm.GetDecimal()
                    : 2.0m;
                return entryPrice - (atrVal * atrMult);
            }

            default:
                return entryPrice * 0.95m;
        }
    }

    private static int ComputeQuantity(
        decimal entryPrice, decimal stopPrice, string sizingType,
        decimal equity, System.Text.Json.JsonElement root)
    {
        switch (sizingType.ToLowerInvariant())
        {
            case "fixed_percentage":
            {
                var riskPct = root.TryGetProperty("RiskPerTradePct", out var rp)
                    ? rp.GetDecimal()
                    : 0.5m;
                var riskPerShare = entryPrice - stopPrice;
                if (riskPerShare <= 0) return 1;

                var riskCapital = equity * (riskPct / 100m);
                return Math.Max(1, (int)(riskCapital / riskPerShare));
            }

            default:
                return 1;
        }
    }
}
