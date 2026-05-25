using System.Text.Json;
using SignalStack.Domain.Backtesting;
using SignalStack.Storage.Historical;

namespace SignalStack.Signals.Backtesting;

/// <summary>
/// Built-in moving average crossover signal evaluator.
/// Entry: fast SMA crosses above slow SMA (golden cross).
/// Exit: fast SMA crosses below slow SMA (death cross).
///
/// Configurable parameters (JSON):
/// {
///   "fast_period": 10,    /// default 10
///   "slow_period": 30,    /// default 30
///   "atr_period": 14,     /// default 14
///   "atr_multiplier": 2.0 /// default 2.0
/// }
/// </summary>
public sealed class MaCrossoverEvaluator : IEntrySignalEvaluator
{
    public string SignalTypeId => "ma_crossover";

    public List<SignalResult> Evaluate(
        IReadOnlyList<OhlcvRecord> candles,
        string? parametersJson)
    {
        var results = new List<SignalResult>();
        if (candles.Count < 2) return results;

        var pars = ParseParameters(parametersJson);
        var fastPeriod = pars.fastPeriod;
        var slowPeriod = pars.slowPeriod;
        var atrPeriod = pars.atrPeriod;
        var atrMult = pars.atrMultiplier;

        // Need enough data for slow SMA
        if (candles.Count <= slowPeriod) return results;

        var fastSma = ComputeSma(candles, fastPeriod);
        var slowSma = ComputeSma(candles, slowPeriod);
        var atrValues = ComputeAtr(candles, atrPeriod);

        bool? wasAbove = null;

        for (var i = slowPeriod; i < candles.Count; i++)
        {
            var isAbove = fastSma[i] > slowSma[i];

            if (wasAbove.HasValue)
            {
                if (!wasAbove.Value && isAbove)
                {
                    // Golden cross — entry signal
                    var atr = atrValues.Count > i ? atrValues[i] : 0;
                    var stopPrice = (decimal)candles[i].Close - (decimal)atr * atrMult;

                    results.Add(new SignalResult
                    {
                        Date = candles[i].Date,
                        Action = SignalAction.Entry,
                        EntryPrice = (decimal)candles[i].Close,
                        StopPrice = stopPrice,
                        Atr = (decimal)atr,
                        Description = $"MA crossover entry: fast={fastSma[i]:F2}, slow={slowSma[i]:F2}"
                    });
                }
                else if (wasAbove.Value && !isAbove)
                {
                    // Death cross — exit signal
                    results.Add(new SignalResult
                    {
                        Date = candles[i].Date,
                        Action = SignalAction.Exit,
                        EntryPrice = (decimal)candles[i].Close,
                        Description = $"MA crossover exit: fast={fastSma[i]:F2}, slow={slowSma[i]:F2}"
                    });
                }
            }

            wasAbove = isAbove;
        }

        return results;
    }

    private static (int fastPeriod, int slowPeriod, int atrPeriod, decimal atrMultiplier) ParseParameters(
        string? json)
    {
        var fast = 10;
        var slow = 30;
        var atrPeriod = 14;
        var atrMult = 2.0m;

        if (string.IsNullOrWhiteSpace(json))
            return (fast, slow, atrPeriod, atrMult);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("fast_period", out var f)) fast = f.GetInt32();
            if (root.TryGetProperty("slow_period", out var s)) slow = s.GetInt32();
            if (root.TryGetProperty("atr_period", out var a)) atrPeriod = a.GetInt32();
            if (root.TryGetProperty("atr_multiplier", out var m)) atrMult = m.GetDecimal();
        }
        catch (JsonException)
        {
            // Use defaults on parse failure
        }

        return (fast, slow, atrPeriod, atrMult);
    }

    private static List<double> ComputeSma(IReadOnlyList<OhlcvRecord> candles, int period)
    {
        var sma = new List<double>(candles.Count);
        double sum = 0;

        for (var i = 0; i < candles.Count; i++)
        {
            sum += candles[i].Close;
            if (i >= period)
                sum -= candles[i - period].Close;

            sma.Add(i >= period - 1 ? sum / period : 0);
        }

        return sma;
    }

    /// <summary>ATR using Wilder's smoothing method (same as RMA).</summary>
    private static List<double> ComputeAtr(IReadOnlyList<OhlcvRecord> candles, int period)
    {
        var atr = new List<double>(candles.Count);

        for (var i = 0; i < candles.Count; i++)
        {
            if (i == 0)
            {
                atr.Add(0);
                continue;
            }

            var high = candles[i].High;
            var low = candles[i].Low;
            var prevClose = candles[i - 1].Close;
            var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));

            if (i < period)
            {
                atr.Add(0);
                continue;
            }

            if (i == period)
            {
                // First ATR is simple mean of first N TR values
                double sumTr = 0;
                for (var j = 1; j <= period; j++)
                {
                    var jHigh = candles[j].High;
                    var jLow = candles[j].Low;
                    var jPrevClose = candles[j - 1].Close;
                    sumTr += Math.Max(jHigh - jLow, Math.Max(Math.Abs(jHigh - jPrevClose), Math.Abs(jLow - jPrevClose)));
                }
                atr.Add(sumTr / period);
            }
            else
            {
                // Wilder's smoothed ATR
                atr.Add((atr[i - 1] * (period - 1) + tr) / period);
            }
        }

        return atr;
    }
}
