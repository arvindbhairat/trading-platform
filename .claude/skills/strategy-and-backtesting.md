# Strategy and Backtesting Skill

Use this guide when implementing strategy modeling, backtesting, or EOD scan behavior.
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md).

## Read These Sections First

- trading calendar and timeframes
- strategy creation, backtesting, and live scans
- position sizing suggestions and risk

## Strategy Focus

- treat strategies as versioned artifacts
- persist the exact parameter set used for each run
- keep backtests reproducible and time-bounded
- keep timeframe aggregation shared with charts and scans
- separate research behavior from live operational workflows

## Checklist

- run inputs and outputs are durable
- failures are diagnosable
- long-running work is resumable or restartable where needed
- live scan behavior respects EOD success conditions
