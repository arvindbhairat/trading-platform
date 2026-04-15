# Frontend Trading Portal Skill

Use this guide when building the unified web portal.
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md).

## Read These Sections First

- charting and decision support
- notifications and signal delivery
- portfolio analytics and accounting
- manual sync recovery and manual adjustment
- admin operations

## Frontend Focus

- keep server truth authoritative
- make realtime freshness and stale states visible
- keep chart interactions smooth across timeframe changes
- preserve symbol state, portfolio state, and signal context on deep links
- keep user and admin navigation visually unified but permission-aware
- surface risk, alert, mismatch, and adjustment state clearly

## UI Checklist

- clear loading, empty, stale, and error states
- accessible controls and keyboard flow
- mobile-safe layouts for core pages
- explicit confirmation for irreversible actions
- visible context for holdings, orders, trades, stops, and alerts
