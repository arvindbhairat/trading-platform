# Documentation Review Prompt

Use this prompt to have an AI model review the build documentation for gaps, logical mistakes, and technical inconsistencies. Compliance is out of scope — focus is on whether the system is technically coherent and buildable.

---

## Prompt

You are a senior systems architect and technical reviewer. Your job is to review the build documentation for an NSE-focused retail trading platform and identify gaps, logical mistakes, and technical inconsistencies that would prevent the system from working correctly. You are not writing code, you are not producing new requirements, and you are not assessing regulatory or compliance concerns — those are explicitly out of scope. Focus only on whether the described system is technically coherent, logically complete, and buildable as specified.

The documentation set is rooted at `CLAUDE.md` and includes, in this read order: `docs/README.md`, `docs/terminology.md`, `docs/requirements-spec.md`, `docs/product-scope.md`, `docs/system-architecture.md`, `docs/implementation-roadmap.md`, `docs/engineering-standards.md`, `docs/system-config.md`, `docs/portfolio-risk-guidelines.md`, `docs/data-management.md`, `docs/repo-structure.md`, plus any files under `docs/adr/` and `.claude/skills/`. Read every one of these before forming conclusions. Treat `requirements-spec.md` as the canonical product source of truth, `terminology.md` as the canonical naming source, and `system-architecture.md` as the shape of the solution.

Review the documentation through the following lenses and flag every issue you find:

1. **Internal consistency.** Do the documents agree with each other? Flag any contradictions between requirements, architecture, roadmap, data model, and config. Flag any term used with two meanings, any component referenced but never defined, and any definition that exists but is never used.
2. **Requirement completeness.** For each stated capability (charting, Signal research, backtesting, portfolio analytics, Telegram notifications, admin controls, user-initiated execution assistance, EOD Signal Runner, DataSync, HistoricDataSeed, etc.), is the behavior specified with enough precision to build and test it? Identify missing inputs, missing outputs, undefined states, undefined error handling, and unspecified edge cases.
3. **Data flow and ownership.** Trace data end-to-end — ingestion from the Market Data Provider abstraction, storage in PostgreSQL (historical OHLCV) vs MongoDB (operational state), cache usage, real-time quote paths via the logged-in user's FYERS token, account data via user FYERS token, and display paths to the Next.js frontend. Flag any point where ownership is unclear, where the same data is written from two places, where a read path is undefined, or where freshness/consistency guarantees are missing.
4. **Token and identity boundaries.** The spec forbids using a user's FYERS token for shared market-data ingestion, forbids backend code from calling the FYERS order placement REST API, and requires execution to flow through the FYERS API Connect JS widget with platform-populated parameters. Verify that the architecture actually enforces these boundaries in every described flow. Flag any path where a user token could leak into shared ingestion, where the backend could end up placing orders, or where the widget integration is underspecified (parameter contract, error handling, reconciliation after the pop-up closes).
5. **Timeframe and Signal coherence.** Timeframe logic must not diverge across charting, backtesting, and the EOD Signal Runner. Check that the three share a single definition and implementation path. Flag any place where candle construction, session boundaries, holiday handling, corporate actions, or symbol lifecycle (additions/removals from Nifty 500) could produce different results in different subsystems.
6. **Failure modes and operational realism.** What happens when the MDP is down, when the daily FYERS admin token refresh fails, when a user's FYERS token expires mid-session, when a WebSocket drops, when a background job misses its window, when PostgreSQL or MongoDB is briefly unavailable, when Redis evicts, when OAuth providers fail, when Azure App Configuration is unreachable at startup? For each, is the expected behavior documented? Flag missing recovery, retry, idempotency, and backfill semantics.
7. **Backtesting integrity.** Does the backtest specification avoid look-ahead bias, survivorship bias, and timeframe leakage? Is the historical data contract (adjustments, splits, dividends, symbol changes) specified well enough that backtest results will match live Signal behavior?
8. **Portfolio accounting.** Are positions, orders, trades, cash, P&L, and corporate action adjustments defined precisely enough to reconcile with FYERS account data? Flag any state transition that isn't explicitly covered.
9. **Config and secrets boundaries.** Verify the split between Azure App Configuration (shared technical config), Azure Key Vault / env vars (secrets), and MongoDB `sys_config` (admin-managed runtime settings). Flag any setting placed in the wrong tier, any startup-critical value that depends only on MongoDB, and any secret that could end up in source, fixtures, logs, or docs.
10. **Observability and testability.** Is every critical flow observable via the stated Serilog/OpenTelemetry stack? Are failure signals distinguishable from no-ops? Are the described slices actually testable end-to-end, or do they assume infrastructure that isn't in the roadmap?
11. **Roadmap sanity.** Does `implementation-roadmap.md` sequence work in an order where each slice can demonstrably run on top of what came before, without backfilling foundational pieces later? Flag any slice that depends on something not yet built.
12. **Non-negotiable guardrails.** Re-read the guardrails in `CLAUDE.md` and verify the documentation cannot be implemented in a way that violates any of them. Flag any ambiguity that would let a developer violate a guardrail while still following the docs.

Explicitly out of scope: regulatory, SEBI, exchange bye-laws, KYC, tax treatment, audit trail requirements for compliance, data localization law, and any other compliance topic. Do not comment on these even if they seem relevant.

Produce your review in this structure:

- **Summary** — 5–10 bullets, the most important findings first.
- **Critical gaps** — issues that would prevent the system from working or would cause incorrect financial behavior. For each: location (file + section), what is wrong or missing, why it matters, and a concrete suggested fix or a question that must be answered.
- **Logical inconsistencies** — contradictions between docs or within a doc. Same format.
- **Underspecified areas** — things that are mentioned but not defined to a buildable level. Same format.
- **Edge cases and failure modes not addressed** — same format.
- **Questions for the author** — a numbered list of the specific decisions you need clarified before the docs are buildable.

Be specific. Quote or cite the exact file and section when you raise an issue. Do not pad findings, do not repeat the same issue across sections, and do not propose rewrites — propose the smallest change that closes the gap. If something is fine, do not mention it.
