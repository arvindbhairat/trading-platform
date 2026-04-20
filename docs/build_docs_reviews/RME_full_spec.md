# Risk Management Engine (RME) – Full Functional Specification

## 1. Overview
The RME is a modular, abstract framework to manage the lifecycle of long positions using technical methodologies. It evaluates multiple techniques, parameters, and timeframes to identify robust configurations.

---

## 2. Core Enhancement: Dynamic Trigger Recalibration

A critical requirement:

> Every time a trigger is hit and an action is executed (add/reduce/exit), the engine MUST recompute and update all future trigger price levels.

### Implications:
- The system is **stateful and iterative**
- Each action creates a **new state snapshot**
- All subsequent levels must reflect:
  - Updated position size
  - Updated average price
  - Updated risk exposure
  - Latest market structure

---

## 3. Trigger Lifecycle Loop

For every candle:

1. Evaluate trigger conditions
2. If trigger hit:
   - Execute action (add/reduce/exit)
   - Transition state
   - Recalculate ALL:
     - Stop loss
     - Trailing stop
     - Add levels
     - Reduce levels
     - Exit levels
3. Continue evaluation with updated state

---

## 4. Scoring System (Decision Layer)

### 4.1 Composite Score

Score =
(0.20 × Normalized CAGR)
+ (0.15 × Sharpe Ratio)
+ (0.15 × Expectancy)
+ (0.10 × Profit Factor)
+ (0.10 × Stability Score)
+ (0.10 × Regime Consistency)
+ (0.10 × Capital Efficiency)
+ (0.10 × Trade Quality)
- (0.10 × Max Drawdown Penalty)

---

### 4.2 Detailed Components

**Expectancy**
Expectancy = (Win% × Avg Win) - (Loss% × Avg Loss)

**Stability Score**
Stability = 1 - (StdDev(Returns across windows) / Mean Return)

**Regime Consistency**
Measured across:
- Bull
- Bear
- Sideways

Score = average normalized performance across regimes

**Capital Efficiency**
= Return / Avg Capital Deployed

**Trade Quality**
Composite of:
- Avg R per trade
- Profit factor
- Win/loss symmetry

**Drawdown Penalty**
Scaled penalty:
Penalty = MaxDD / Threshold

---

### 4.3 Robustness Factor

Final Score = Composite Score × Robustness Factor

Robustness Factor includes:

- Parameter Sensitivity Score
- Cross-Symbol Consistency
- Walk-forward Stability

---

## 5. Parameter Sensitivity (Critical)

A configuration is penalized if:
- Small parameter changes ? large performance drop

Sensitivity Score:
= 1 - (Variance of results across neighboring parameter sets)

---

## 6. Cross-Symbol Validation

Configuration must:
- Perform consistently across multiple symbols
- Avoid single-stock overfitting

---

## 7. Multi-Timeframe Evaluation

Engine evaluates:

- Daily
- Weekly
- Monthly
- Rolling N-day (3,5,7,10,15…)

And combinations:
- Weekly trend + Daily execution
- Rolling windows + Daily

---

## 8. Final Output

For each symbol:

- Best Technique
- Optimal Parameters
- Recommended Timeframe
- Composite Score
- Robustness Score

---

## 9. Actionable Output

At any point, engine must output:

- Stop Loss Level
- Trailing Stop Level
- Add Trigger(s)
- Reduce Trigger(s)
- Exit Trigger

AND after ANY trigger execution:
? ALL levels must be recalculated

---

## 10. Summary

The RME is a dynamic, adaptive system that:

- Continuously recalibrates decisions
- Evaluates probabilistic outcomes
- Avoids static rule dependency
- Produces actionable price levels
- Identifies robust configurations across timeframes and symbols
