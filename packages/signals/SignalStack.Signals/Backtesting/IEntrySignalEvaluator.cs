using SignalStack.Domain.Backtesting;
using SignalStack.Storage.Historical;

namespace SignalStack.Signals.Backtesting;

/// <summary>
/// Pluggable signal evaluation strategy used by the backtest engine.
/// Evaluates a set of OHLCV candles for a single symbol and returns
/// entry/exit signals for each candle.
///
/// portfolio-risk-guidelines § Backtesting: the same RME logic runs in
/// both backtest and live modes. Signal evaluation is pluggable per
/// Signal type.
/// </summary>
public interface IEntrySignalEvaluator
{
    /// <summary>Returns the Signal type identifier this evaluator handles.</summary>
    string SignalTypeId { get; }

    /// <summary>
    /// Evaluates the given candles and produces signal results per candle.
    /// Candles are in chronological order (oldest first).
    /// </summary>
    /// <param name="candles">OHLCV records sorted by date ascending.</param>
    /// <param name="parameters">Signal-type-specific configuration.</param>
    /// <returns>A signal result for each candle after the warmup period.</returns>
    List<SignalResult> Evaluate(
        IReadOnlyList<OhlcvRecord> candles,
        string? parametersJson);
}
