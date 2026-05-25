namespace SignalStack.Storage.Backtesting;

/// <summary>
/// Execution cost model for backtesting: applies slippage and commission
/// to simulated trades. portfolio-risk-guidelines § Backtesting Rules:
/// results must never be reported without explicit slippage and commission.
///
/// REQ-STRAT-018: Backtest results must never be reported without the
/// configured slippage and commission assumptions applied.
/// </summary>
public sealed class ExecutionModel
{
    /// <summary>Slippage as a fraction of price (e.g., 0.001 = 0.1%).</summary>
    public decimal SlippageFraction { get; }

    /// <summary>Commission as a fraction of trade value (e.g., 0.0001 = 0.01%).</summary>
    public decimal CommissionFraction { get; }

    /// <summary>Round-trip commission multiplier (entry + exit = 2 commissions).</summary>
    private const int CommissionMultiplier = 2;

    public ExecutionModel(decimal slippageFraction, decimal commissionFraction)
    {
        if (slippageFraction < 0)
            throw new ArgumentException("Slippage must be non-negative.", nameof(slippageFraction));
        if (commissionFraction < 0)
            throw new ArgumentException("Commission must be non-negative.", nameof(commissionFraction));

        SlippageFraction = slippageFraction;
        CommissionFraction = commissionFraction;
    }

    /// <summary>
    /// Calculates the effective entry price including slippage.
    /// For long entries, slippage adds to the price.
    /// </summary>
    public decimal ApplyEntrySlippage(decimal signalPrice)
    {
        return signalPrice * (1m + SlippageFraction);
    }

    /// <summary>
    /// Calculates the effective exit price including slippage.
    /// For long exits, slippage subtracts from the price.
    /// </summary>
    public decimal ApplyExitSlippage(decimal exitPrice)
    {
        return exitPrice * (1m - SlippageFraction);
    }

    /// <summary>
    /// Calculates total commission for a round-trip trade (entry + exit).
    /// </summary>
    public decimal CalculateCommission(decimal entryValue, decimal exitValue)
    {
        return (entryValue + exitValue) * CommissionFraction;
    }

    /// <summary>
    /// Calculates net PnL for a round-trip trade after slippage and commission.
    /// </summary>
    public decimal CalculateNetPnL(decimal grossPnl, decimal entryValue, decimal exitValue)
    {
        return grossPnl - CalculateCommission(entryValue, exitValue);
    }

    /// <summary>
    /// Models gap risk at session open per portfolio-risk-guidelines § Backtesting:
    /// when price gaps past the stop level at session open, use the gap open
    /// price as the effective fill.
    /// </summary>
    public static decimal ModelGapExit(decimal openPrice, decimal stopPrice, decimal slippageFraction)
    {
        var effectiveExit = openPrice * (1m - slippageFraction);
        // If the gap is adverse (open past stop), use the gap-open price minus slippage
        // If open is worse than stop, use the open-adjusted price
        return openPrice < stopPrice
            ? Math.Min(effectiveExit, stopPrice)
            : effectiveExit;
    }

    public override string ToString()
        => $"Slippage={SlippageFraction:P2}, Commission={CommissionFraction:P2}";
}
