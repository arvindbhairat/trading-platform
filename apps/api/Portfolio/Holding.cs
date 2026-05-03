namespace SignalStack.Api.Portfolio;

/// <summary>
/// A computed holding derived from FIFO matching of the trade ledger.
/// Represents a currently open position in a single symbol.
/// </summary>
public sealed record Holding
{
    /// <summary>NSE trading symbol (e.g. "RELIANCE").</summary>
    public required string Symbol { get; init; }

    /// <summary>Net quantity held (all long lots summed after FIFO matching).</summary>
    public required int Quantity { get; init; }

    /// <summary>Average buy price (cost basis) in INR.</summary>
    public required decimal AverageBuyPrice { get; init; }

    /// <summary>Total cost basis (AverageBuyPrice × Quantity).</summary>
    public decimal TotalInvested => AverageBuyPrice * Quantity;

    /// <summary>Current market price in INR, or null if unavailable.</summary>
    public decimal? CurrentPrice { get; init; }

    /// <summary>Current market value (CurrentPrice × Quantity), or null.</summary>
    public decimal? CurrentMarketValue =>
        CurrentPrice.HasValue ? CurrentPrice.Value * Quantity : null;

    /// <summary>Unrealised PnL in INR (CurrentMarketValue - TotalInvested), or null.</summary>
    public decimal? UnrealizedPnl =>
        CurrentMarketValue.HasValue ? CurrentMarketValue.Value - TotalInvested : null;

    /// <summary>Unrealised PnL as percentage of cost basis, or null.</summary>
    public decimal? UnrealizedPnlPercent =>
        AverageBuyPrice > 0 && CurrentPrice.HasValue
            ? Math.Round(((CurrentPrice.Value - AverageBuyPrice) / AverageBuyPrice) * 100, 2)
            : null;

    /// <summary>Holding period in calendar days from first buy.</summary>
    public required int HoldingPeriodDays { get; init; }

    /// <summary>Number of lots currently held after FIFO matching.</summary>
    public required IReadOnlyList<OpenLot> Lots { get; init; }
}

/// <summary>
/// A single open lot within a holding — represents a specific buy trade
/// that has not been fully matched by sell trades.
/// </summary>
public sealed record OpenLot
{
    /// <summary>Trade ID from the original buy trade.</summary>
    public required string TradeId { get; init; }

    /// <summary>Number of shares remaining in this lot.</summary>
    public required int RemainingQuantity { get; init; }

    /// <summary>Buy price of this lot in INR.</summary>
    public required decimal BuyPrice { get; init; }

    /// <summary>Trade date (UTC).</summary>
    public required DateTime BuyDate { get; init; }
}

/// <summary>
/// Portfolio-level summary computed from all open holdings.
/// </summary>
public sealed record PortfolioSummary
{
    /// <summary>Total cost basis across all holdings (sum of TotalInvested).</summary>
    public required decimal TotalInvested { get; init; }

    /// <summary>Total current market value across all holdings, or null if any price is missing.</summary>
    public decimal? TotalMarketValue { get; init; }

    /// <summary>Total unrealised PnL, or null.</summary>
    public decimal? TotalUnrealizedPnl { get; init; }

    /// <summary>Total unrealised PnL as percentage of total invested, or null.</summary>
    public decimal? TotalUnrealizedPnlPercent { get; init; }

    /// <summary>Number of open positions (holdings with Quantity &gt; 0).</summary>
    public required int PositionCount { get; init; }
}
