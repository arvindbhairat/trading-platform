# Portfolio Risk Guidelines

## Purpose

This document defines the default risk framework for the Risk Management Engine (RME) across strategy design, backtesting, signal generation, and live workflows.
It elaborates the canonical risk requirements in [requirements-spec.md](./requirements-spec.md); if there is a conflict, the requirements spec wins.
Claude should enforce these rules unless a feature explicitly documents a justified exception.

## Core Principle

Protect capital first. A strategy that survives bad conditions is more valuable than one that performs well only in ideal backtests. Risk management takes priority over return optimisation.

## Risk Units (R)

All risk calculations must be based on Risk Units.

1R is defined as the cash amount lost if the stop loss is hit at the entry price.

All performance metrics must be expressed in R terms:

- R multiple per trade
- Average R across all trades
- Expectancy in R
- Drawdown in R

This makes performance comparable across different sizing configurations and strategies.

## Design Principles for the RME

The Risk Management Engine must follow these principles without exception:

1. Risk management takes priority over return optimisation.
2. Position sizing must be driven by risk, not capital allocation alone.
3. Portfolio risk is more important than individual trade risk.
4. Behaviour must be consistent between backtesting and live signal workflows.
5. The engine must be event-driven.
6. The engine must be strategy-independent.
7. The engine must be configurable and modular.
8. All calculations must be based on current account equity.
9. The system must operate in Risk Units (R).
10. Averaging down must be disabled by default.
11. Total portfolio risk must always be known.
12. The engine must be deterministic: identical inputs produce identical outputs.

## Trade-Level Risk Rules

For each position the RME tracks:

- Entry price and date
- Initial stop loss price
- Position size and average entry price
- Initial risk amount (1R)
- Current R multiple
- Active trailing stop level
- Break-even stop level if triggered
- Add levels and add quantities
- Reduce levels and reduce quantities
- Maximum allowed position size for that strategy
- Holding period and time stop date if applicable
- ATR at entry and current ATR
- Portfolio heat contribution

### Default Risk Per Trade

- Cap risk per trade at 0.5% to 1.0% of current account equity by default.
- Default sizing model is Fixed Percentage Risk Per Trade.
- Cap single stock exposure at 10% of portfolio equity.

## Portfolio-Level Risk Rules

Across all open positions the RME must maintain:

- Total capital, used capital, and available capital
- Total open risk and portfolio heat percentage
- Number of open positions
- Exposure per stock and per sector
- Largest position as a percentage of equity
- Cash percentage
- Net and gross exposure

### Default Portfolio Limits

- Cap total open risk across all positions at 5% of portfolio equity by default.
- Cap sector or correlated-basket exposure at 20% of portfolio equity by default.
- Minimum cash reserve percentage is configurable; the default should be conservative.

## Portfolio Heat

Portfolio Heat = Total Open Risk / Account Equity, expressed as a percentage.

The RME must:

- Calculate and maintain portfolio heat continuously.
- Block new trade signal recommendations when heat would exceed the configured maximum after the proposed position.
- Trigger position reduction advisories when heat is at or approaching the configured maximum.
- Prevent add-on recommendations in the same sector when heat is elevated. Sector concentration is the V1 proxy for correlation; statistical cross-position correlation modelling is a future enhancement.

## Account-Level Risk and Drawdown Control

The RME must track account drawdown from the equity high-water mark and apply graduated responses.

### Drawdown Response Thresholds

| Drawdown | Response |
|----------|----------|
| 5% | Reduce recommended new position size proportionally |
| 10% | Suspend add-on entry recommendations |
| 15% | Surface advisory to reduce all open positions |
| 20% | Block new trade signal recommendations |
| 25% | Surface advisory to close weakest open positions |
| 30% | Surface advisory to stop all trading |

All thresholds are configurable through `sys_config`. The values above are safe defaults.

### Loss Limits

Daily, weekly, and monthly loss limits must be configurable and monitored. Breaching any limit surfaces an advisory to the user.

### Equity Curve Tracking

The platform must maintain a running equity curve. Equity-curve-based position sizing adjustment is a configurable option on top of the primary sizing model.

## Position Sizing Models

The RME must support multiple pluggable sizing models sharing a common interface. Each model receives the same standard inputs and produces a recommended position size.

### Required Models (V1)

- Fixed Percentage Risk Per Trade (default)
- ATR and Volatility-Based Sizing
- Portfolio Heat-Based Sizing
- Drawdown-Adjusted Sizing

### Optional Models (Future)

- Fixed Percentage Risk with Pyramiding
- Equal Risk Contribution
- Trend Following Pyramiding
- Breakout Add-on
- Kelly Fraction
- Rebalancing-based Sizing

### Z-Score Advisory Overlay

Z-score deviation-based suggestions remain supported as an overlay signal on top of the primary sizing model.

- Compute z-score from rolling 3-day or 5-day trading-session candles built from historical data.
- Default to rolling 5-day candles.
- Default add-more threshold is `+3`; default reduce-size threshold is `-3`.
- Map the thresholds to price levels and surface those levels as advisory add-more and reduce-size targets.
- Always apply portfolio heat and risk cap checks before surfacing any add-more advisory.
- Expose the timeframe, z-score value, threshold, and derived price level behind each suggestion.

## Stop Loss Types

The RME must support multiple stop loss types configurable per strategy.

### Required Stop Types (V1)

- Fixed Stop Loss — stop set at a fixed price below entry
- ATR-based Stop — stop set at a multiple of ATR below entry
- Trailing Stop (percentage) — stop trails entry price by a fixed percentage
- Trailing Stop (ATR) — stop trails by an ATR multiple, updated on each session close
- Swing Low Stop — stop placed below the most recent swing low
- Break-Even Stop — stop moved to entry price once position reaches a configurable profit level (default 1R)

### Additional Stop Types (Future)

- Time Stop — exit after a defined holding period regardless of price
- Volatility Stop — tighten stop when volatility spikes beyond a threshold
- Portfolio Stop — reduce position when portfolio heat limit is breached
- Equity Curve Stop — reduce sizing or exit when equity curve deteriorates
- Gap Risk Stop — treat gap-open past stop as the effective fill price
- Circuit Limit Stop — alert and suspend when NSE circuit limits are triggered

### Exit Priority

When multiple exit conditions are triggered simultaneously, the following priority order must be applied:

1. Catastrophic gap or circuit limit breach
2. Hard stop loss
3. Trailing stop
4. Portfolio risk reduction (heat limit)
5. Drawdown control exit
6. Time stop
7. Strategy exit signal
8. Rebalancing exit
9. Profit target exit

## Pyramiding and Scaling Rules

### Adding to a Position

- Add at a fixed percentage move from entry, at R multiples, at ATR multiples, on breakout above resistance, or on pullback to support.
- Add-on quantity must reduce progressively with each successive add; add size must never increase.
- Total position risk including all adds must never exceed the configured maximum per-position risk.
- Add recommendations must be blocked when portfolio heat would exceed maximum after the add.
- Maximum adds per position is configurable.

### Reducing a Position

- Reduce at configured profit targets, when trend weakens per the strategy signal, when volatility increases beyond a threshold, when portfolio heat breaches the maximum, or after a configured time period.
- Reduce the weakest positions first when a portfolio-level reduction is advised.

### After Each Add or Reduce

Recalculate immediately: average entry price, active stop level, current R multiple, and portfolio heat contribution.

## Position Lifecycle States

Each position is a lifecycle object with formal, event-driven state transitions. Advisory conditions within an open position are tracked as flags on the record, not as separate states.

| State | Description |
|-------|-------------|
| PendingEntry | Signal generated, entry not yet confirmed |
| Open | Position is active; advisory flags indicate current advisory conditions |
| Closed | Position fully closed |
| Rejected | Trade blocked by RME rules before entry |
| Suspended | Position frozen due to a risk event (circuit limit, extreme gap, drawdown threshold) |

Advisory flags on an Open position (not states):

| Flag | Set when |
|------|----------|
| add advisory active | Add level reached; advisory surfaced to user |
| reduce advisory active | Reduce level reached; advisory surfaced to user |
| trailing stop active | Trailing stop is updating on each session close |
| exit advisory active | Exit condition triggered; advisory surfaced to user |

Allowed transitions: PendingEntry → Open or Rejected; Open → Closed or Suspended; any state → Suspended. Advisory flags are updated in response to price and portfolio events without triggering a state transition.

## Signal Quality Rules

- Do not emit duplicate live signals for the same strategy, symbol, and decision window unless explicitly configured to do so.
- Suppress signals when market data is stale, incomplete, or outside expected trading session rules.
- Suppress add-on signals when drawdown mode is active at the blocking threshold.
- Record why a signal was allowed, blocked, downsized, or rejected.
- Reject short-entry or short-exit strategy behaviour; the platform is long-only.

## Backtesting Rules

- Apply slippage and commission assumptions explicitly and consistently; results must never be reported without them.
- Model gap risk: when price gaps past the stop level at session open, use the gap open price as the effective fill.
- Report at minimum: total return, win rate, average R, expectancy in R, profit factor, maximum drawdown, drawdown in R, Sharpe ratio, Sortino ratio, recovery factor, and total number of trades.
- Flag runs with too few trades or too little historical coverage as low-confidence.
- Keep slippage, commission, and sizing assumptions visible in the result record.

## Operational Risk Controls

- Global kill switch: admin can suspend all strategy signal generation platform-wide.
- Per-user and per-strategy disable controls must be available.
- Admin approval is required before a user can access protected platform features.
- FYERS credential failures and Telegram delivery failures are observable operational events and must be logged and surfaced.
- Intraday stop monitoring uses each user's own FYERS token; monitoring for a user is suspended when their token is dirty or expired, with a non-blocking portal warning shown. The monitoring interval is configurable via `sys_config` and the job must only run during NSE market hours.

## Required Auditability

- Persist the RME configuration and risk parameter set used when any strategy run or backtest was executed.
- Log why each strategy signal was allowed, blocked, or downsized.
- Log why each sizing suggestion was generated, including the model, inputs, and thresholds.
- Persist the drawdown level and portfolio heat at the time each signal decision was made.

## Implementation Guidance

- Keep the RME as a dedicated, independently testable server-side module.
- The RME must not depend on strategy-specific indicator logic; it receives indicator outputs as inputs.
- Make all thresholds configurable but default-safe.
- Test sizing logic with edge cases: tiny stop distance, missing prices, low capital, portfolio at maximum heat.
- Ensure backtest and live signal workflows share one RME implementation, not two separate ones.
- Keep execution reality modelling (slippage, gap fills) isolated within the backtest engine layer so it does not contaminate live advisory output.
