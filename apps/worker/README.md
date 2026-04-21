## apps/worker

.NET background job runner for backtests, live strategy execution, notifications, and sync jobs.

Preferred internal layout (see `docs/repo-structure.md`) lives under `worker/`:
- `worker/jobs`: backtest, signal, notification, sync jobs
- `worker/runners`: scheduling + execution entry points
- `worker/lib`: worker utilities + observability helpers
- `worker/config`: bootstrap config and runtime setting access

