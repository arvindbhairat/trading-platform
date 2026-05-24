# Product Scope

## Purpose

This file is a short overview of the product.
For exact business rules, workflow details, UI requirements, and edge cases, use [requirements-spec.md](./requirements-spec.md).

## Product Goal

Build a unified browser-based platform for trading research and decision support over NSE Nifty 500 stocks.
The platform combines charting, backtesting, live scans, portfolio analytics, admin controls, and Telegram notifications in one portal.

## Product Identity

- long-only platform
- NSE Nifty 500 universe only
- unified portal for both users and the single admin
- notification-first and decision-support-first
- user-initiated execution assistance only; no autonomous order placement (V1 delivery of in-portal execution assistance is conditional on the FYERS CNC sandbox verification defined in REQ-ORDER-010b; if that verification fails, REQ-ORDER-010a applies and V1 ships advisory-only with a deep-link handoff to the FYERS app or web platform, while the "no autonomous order placement" stance remains absolute in every configuration)
- FYERS for broker and market-data integration
- MongoDB for live and user data, PostgreSQL for shared historical data
- Azure-hosted delivery with GitHub (GitHub Actions for CI/CD)

## Primary Users

- trader: charting, Signals, backtests, portfolio analytics
- admin: all trader capabilities plus user approval, universe management, calendar management, and job controls

## V1 Summary

V1 focuses on:

- OAuth sign-in and admin approval
- FYERS authentication and account sync
- admin-managed Nifty 500 universe and internal trading calendar
- shared PostgreSQL historical data plus EOD sync
- charting with shared timeframe semantics
- Signal creation as after-market opportunity scanners generating entry signals
- automated RME profile optimisation backtests with mechanism comparison and symbol-level recommendations (delivered as part of the RME phase, after core RME is operational)
- Risk Management Engine with pluggable position sizing, stop loss, trailing stop, and level generation mechanisms
- RME profile bound to each position at entry, governing the full lifecycle until exit
- portfolio impact assessments surfaced before each entry advisory
- Telegram signal delivery with brief portfolio impact summary and chart deep links
- portfolio analytics with FIFO, gross PnL, XIRR, and R-based performance metrics
- user dashboard showing portfolio summary, period performance, sector breakdown, positions summary, and RME health indicators
- trailing-stop monitoring, position lifecycle management, and RME advisory surfacing
- admin job controls, auditability, and reconciliation support

## Next Phase Summary

After V1 is stable, the next phase should add:

- corporate actions and symbol continuity handling
- richer charting and analytics
- broader execution-readiness work behind feature flags
- more advanced portfolio and account features

## Out of Scope for Early Milestones

- options, futures, and other derivatives
- short selling or short-side trading strategy logic
- instruments outside NSE Nifty 500
- autonomous execution
- tax reporting
- mobile-native apps

## Key User Journeys

1. User signs in with OAuth, waits for approval, connects FYERS, and lands on the dashboard home page.
2. User reviews the dashboard: portfolio summary, XIRR, period performance, sector breakdown, positions summary, and RME health strip.
3. User creates a Signal Subscription as an after-market opportunity scanner, runs automated RME profile optimisation backtests across mechanism combinations, and reviews symbol-level recommendations.
4. User enables the EOD Signal Runner and receives Telegram signals with brief portfolio impact summary and chart deep links.
5. User opens the chart page from Telegram, reviews the full RME advisory including entry rationale, stop level, add and reduce levels, and portfolio impact breakdown, then acts via FYERS API Connect if they choose.
6. User monitors open positions on the dashboard and via Telegram alerts as the RME tracks stop updates, add and reduce advisories, and exit conditions through the position lifecycle.
7. Admin approves users, uploads the Nifty 500 CSV, manages the trading calendar, and retries failed jobs when needed.

## How to Use the Docs

- use [requirements-spec.md](./requirements-spec.md) for exact requirements
- use [system-architecture.md](./system-architecture.md) for system design
- use [implementation-roadmap.md](./implementation-roadmap.md) for delivery sequencing
- use [engineering-standards.md](./engineering-standards.md) for build and operational conventions
