# Build Documentation Review Prompt

Use this prompt to have a senior reviewer read the full build documentation set and surface gaps, weaknesses, and risks **before** implementation begins in earnest. The goal is a buildable, stable, and genuinely useful system — not an academically complete one.

Compliance, regulatory, SEBI, exchange bye-laws, KYC, tax, data localization, and audit-for-compliance concerns are **explicitly out of scope** for this review. Those will be addressed in a separate pass once the system has been built, back-tested, used for personal trading, and piloted with a small group.

---

## Skills to apply during this review

While performing this review, you may internally apply the following skills as appropriate to the finding being evaluated. Use them **implicitly** as review lenses — do not reference skill names explicitly in the output.

- **Architecture review** — for evaluating system structure, component boundaries, architectural decisions, and ADR alignment.
- **System design review** — for assessing scale assumptions, phase-aligned design choices, invariants, and data/control flow clarity.
- **Documentation review** — for clarity, precision, internal consistency, and buildability from the written specs.
- **Testing strategy review** — for testability, backtest/live parity, edge case coverage, and verification of critical behaviors.
- **Technical debt assessment** — for identifying complexity traps, premature abstraction, and decisions that will become expensive to unwind.
- **Deployment readiness review** — for operational realism, startup ordering, config/secrets boundaries, and failure handling.
- **Incident response perspective** — for evaluating diagnosability, observability, recovery paths, and operator visibility during failures.

Do not invent new requirements or features. Apply these skills conservatively and only where they materially affect correctness, trader trust, or system safety.

---

## Prompt

You are reviewing the build documentation for an NSE Nifty 500–focused retail trading platform. The platform provides charting, Signal research, backtesting, portfolio analytics, Telegram notifications, admin controls, and **user-initiated** execution assistance. Orders are never placed by the system autonomously; execution always flows through the FYERS API Connect JS widget with parameters pre-populated by the platform frontend.

You are acting simultaneously as four reviewers in one:

1. A **senior technical reviewer** — evaluating whether the described system is logically coherent, internally consistent, and buildable as specified.
2. A **software/solution architect** — evaluating whether the architecture is appropriate for the scale, team size, and phased rollout, and whether it will remain maintainable as it grows.
3. An **application security reviewer** — evaluating authentication, authorization, token handling, data protection, and abuse resistance.
4. A **professional discretionary/systematic trader** — evaluating whether the platform would actually be usable, trustworthy, and decision-supporting in a live market session, and whether the Risk Management Engine behaves the way a serious trader needs it to.

Read the full documentation set before forming conclusions. The read order, rooted at `CLAUDE.md`, is:

1. `docs/README.md`
2. `docs/terminology.md`
3. `docs/requirements-spec.md`
4. `docs/product-scope.md`
5. `docs/system-architecture.md`
6. `docs/implementation-roadmap.md`
7. `docs/engineering-standards.md`
8. `docs/system-config.md`
9. `docs/portfolio-risk-guidelines.md`
10. `docs/data-management.md`
11. `docs/repo-structure.md`

Also read every file under `docs/adr/` and every skill file under `.claude/skills/`. Treat `requirements-spec.md` as the canonical product source of truth, `terminology.md` as the canonical naming source, `system-architecture.md` as the shape of the solution, and the **Non-Negotiable Guardrails** in `CLAUDE.md` as hard constraints that the documentation must not allow a developer to violate.

### Context you must hold while reviewing

- The primary developer is strong in C# and TypeScript and new to Node.js full-stack patterns. The frontend is intentionally thin; business logic lives in the .NET backend.
- The rollout is phased: personal trading → small closed pilot for feedback → commercial launch. Review through this lens — early phases favor simplicity and observability over scale.
- Market data is accessed via a configurable **Market Data Provider (MDP)** abstraction. FYERS is the initial provider because its market-data APIs are free during evaluation only; the platform will migrate to a commercial provider (TrueData, GDF, or equivalent) before broader rollout. Provider-specific quirks must remain encapsulated behind the MDP abstraction.
- Historical OHLCV lives in SQL Server. Operational state lives in MongoDB. Admin-managed runtime settings live in `sys_config` in MongoDB. Shared technical config lives in Azure App Configuration. Secrets live in Azure Key Vault or environment variables.
- Live quotes and real-time chart data use the **individual logged-in user's** FYERS token at display time. Shared market-data ingestion (DataSync, HistoricDataSeed, EOD Signal Runner) must **never** use a regular user's token.
- Backend services access FYERS exclusively via REST. The FYERS Data WebSocket is permitted only in the browser tier.
- The Worker Service is single-instance by design until the Phase C multi-instance partitioning ADR is delivered. Scale-out silently breaks the per-position channel invariant.

### Overarching principle: do not over-engineer

The user has explicitly asked that this review not push the system toward unnecessary complexity. Before flagging anything as a gap, ask yourself:

- Does closing this gap materially reduce the risk of incorrect trading decisions, data loss, security compromise, or unbuildability?
- Or is it theoretical completeness that would slow down delivery without changing outcomes?

Flag only the former. If a section is simple and sufficient for the current phase, leave it alone. If a proposed feature reads as over-engineered for a platform that is initially for a single trader and a small pilot, **call that out explicitly** as a finding in its own right.

---

### Review lenses

Work through every lens. For each finding, cite the exact file and section, and propose the **smallest** change that closes the gap.

#### A. Technical coherence

1. **Internal consistency.** Do requirements, architecture, roadmap, data model, config, and ADRs agree? Flag contradictions, terms used with two meanings, components referenced but never defined, and definitions that exist but are never used.
2. **Requirement completeness — functional.** For each capability (charting, Signal research, backtesting, portfolio analytics, Telegram notifications, admin controls, user-initiated execution assistance, EOD Signal Runner, DataSync, HistoricDataSeed, Risk Management Engine), is the behavior specified with enough precision to build and test it? Identify missing inputs, missing outputs, undefined states, undefined error handling, and unspecified edge cases.
3. **Requirement completeness — non-functional.** Are latency budgets, throughput expectations, data freshness guarantees, availability targets, recovery time objectives, and capacity assumptions specified where they matter? Flag NFRs that are missing *and* would actually affect a real trading session — ignore NFRs that are decorative.
4. **Data flow and ownership.** Trace data end-to-end — ingestion through the MDP abstraction, storage in SQL Server vs MongoDB, cache usage, real-time quote paths via the user's FYERS token, account data via the user's FYERS token, and display paths to the Next.js frontend. Flag any point where ownership is unclear, the same data is written from two places, a read path is undefined, or freshness/consistency guarantees are missing.
5. **Timeframe and Signal coherence.** Timeframe logic must not diverge across charting, backtesting, and the EOD Signal Runner. Flag any place where candle construction, session boundaries, holiday handling, corporate actions, or Nifty 500 constituent changes could produce different results in different subsystems.
6. **Backtesting integrity.** Check for look-ahead bias, survivorship bias, timeframe leakage, and inconsistent handling of splits, dividends, bonuses, and symbol changes between backtest and live. Flag any case where backtest results would not match live Signal behavior on the same bar.
7. **Portfolio accounting.** Are positions, orders, trades, cash, realized/unrealized P&L, average price, and corporate action adjustments defined precisely enough to reconcile with FYERS account data? Flag any state transition that isn't explicitly covered, especially partial fills, rejections, cancels, modifies, and reconciliation after the FYERS widget pop-up closes.
8. **Roadmap sanity.** Does `implementation-roadmap.md` sequence work so each slice demonstrably runs on what came before, without backfilling foundational pieces later? Flag slices that depend on something not yet built.

#### B. Architectural soundness

9. **Right-sizing for the phase.** Is the architecture appropriate for single-trader → small-pilot → commercial, or does it pay commercial-scale costs too early? Flag over-engineering (unnecessary services, premature distribution, speculative abstractions) and under-engineering (shortcuts that will be painful to unwind at pilot scale) with equal rigor.
10. **Component boundaries.** Are the responsibilities of the API service, Worker Service, frontend, MDP adapters, Risk Management Engine, Signal Runner, and backtester cleanly separated? Flag any component that owns two unrelated concerns or any concern that is split across components without a clear contract.
11. **MDP abstraction integrity.** Does the abstraction actually hide FYERS-specific quirks, or do they leak into callers? Can a different provider be swapped in by changing configuration and one adapter, as the guardrails require?
12. **Single-instance Worker Service invariant.** Is the single-instance constraint visible everywhere a reader might otherwise assume horizontal scale? Are Azure App Service scale-out, auto-scale, and VM scale-set controls called out as disabled? Flag any doc or diagram that implies the Worker is horizontally scalable today.
13. **Config and secrets boundaries.** Verify the split between Azure App Configuration, Azure Key Vault / env vars, and MongoDB `sys_config`. Flag any setting in the wrong tier, any startup-critical value that depends only on MongoDB, and any secret that could end up in source, fixtures, logs, or docs.
14. **Observability and testability.** Is every critical flow observable via Serilog and OpenTelemetry? Are failure signals distinguishable from no-ops? Are the described slices actually testable end-to-end, or do they assume infrastructure that isn't in the roadmap?

#### C. Security posture (application + data, not regulatory)

15. **Token and identity boundaries.** The spec forbids using a user's FYERS token for shared market-data ingestion, forbids backend code from calling the FYERS order placement REST API, and requires execution to flow through the FYERS API Connect JS widget with platform-populated parameters. Verify the architecture enforces these in every described flow. Flag any path where a user token could leak into shared ingestion, where the backend could end up placing orders, or where the widget integration is underspecified (parameter contract, error handling, reconciliation after the pop-up closes).
16. **Auth and session.** Is OAuth (Google, Microsoft, Facebook/Meta) integration specified with enough detail — session lifetime, refresh, logout, account linking, revocation? Flag any server-side validation that could be bypassed from a crafted client.
17. **Authorization.** Are admin-only operations, user-scoped operations, and read-only operations clearly separated and enforced server-side? Flag any admin surface that relies on frontend enforcement only.
18. **Secret handling.** Can secrets appear in logs, traces, error responses, stack traces, cache keys, metric tags, or shipped fixtures? Flag the specific path.
19. **Data protection.** Is user-identifying data and portfolio data encrypted in transit and at rest per the stated stack? Are backups covered? Flag any data class whose protection level isn't stated.
20. **Abuse and rate limiting.** Are there sensible limits on charting requests, backtest runs, Signal queries, and Telegram notifications? Flag flows that a single authenticated user could trivially turn into a cost or stability incident.
21. **Supply chain and build.** Does the build pipeline pin dependencies and produce reproducible artifacts? Flag any step that would let an attacker inject code into a release without detection.

#### D. Professional trader's perspective

Evaluate the system the way a trader actually using it would. A trader does not care about elegance — they care about trust, speed of decision, clarity of state, and whether the system protects them from themselves on a bad day.

22. **Risk Management Engine (RME) — this is the highest-priority section of the review.** The RME is purely **advisory** — it must never place or modify orders. It must be:
    - **Robust** under missing data, stale data, partial fills, and mid-session token expiry.
    - **Flexible** enough to express common trader rules: per-trade risk cap, per-position stop, trailing stop, time-based exit, volatility-based sizing, max daily loss, max open positions, max concentration per sector/symbol, correlation-aware exposure, portfolio heat, and staged entries/exits.
    - **Backtestable** — every rule must run identically in backtest and live, against the same historical data contract.
    - **Full-lifecycle** — pre-trade (position sizing, entry checks), in-trade (stop management, add-on rules, partial exits), and post-trade (realized P&L, journaling, rule attribution). Flag any lifecycle stage where the RME is silent.
    - **Transparent** — a trader must be able to see *why* the RME produced a given recommendation, including the inputs, the rule that fired, and the numbers.
    - **Advisory-only and obviously so in the UI** — recommendations must be clearly distinguishable from confirmed user actions, and nothing the RME emits must be auto-actionable.
    
    Review the docs for each of these properties. Flag any RME rule that is vaguely described, any rule that cannot be backtested, any rule whose live behavior could diverge from its backtest behavior, any place where the RME could be interpreted as authoritative rather than advisory, and any missing rule that a serious NSE equity trader would expect on day one.

23. **Position lifecycle.** Can the platform represent the full life of a position from pre-trade intent → pending order (via widget) → open position → scale-in / scale-out → adjusted stop → exit → journaled trade? Flag any lifecycle stage that the data model cannot represent cleanly.
24. **Decision latency.** In a live session, how fast does a trader get from "I want to see this chart" to seeing the current quote and the RME's view? Flag any round-trip that will feel slow in practice, and any place where the user has to wait on a background job for a decision-time answer.
25. **State clarity under partial information.** What does the UI show when a quote is stale, when a token is about to expire, when a background sync missed its window, when a corporate action has not yet been applied, or when the widget pop-up closed without a confirmation? Flag any state where the trader is left guessing.
26. **Execution assistance ergonomics.** Are the parameters the platform pre-populates into the FYERS widget documented clearly — symbol, side, quantity, order type, limit price, stop, product code, validity? Is the reconciliation after the pop-up closes specified? Flag any ambiguity that would cause a trader to second-guess whether their order went through.
27. **Signal trust.** A trader will not follow a Signal they cannot audit. Are Signals reproducible from historical data, attributable to a specific rule, and inspectable bar-by-bar? Flag any Signal surface where the trader cannot answer "why did this fire today?".
28. **Notifications as a trading tool, not spam.** Are Telegram notifications specified with enough precision that they remain actionable (what fires, when, with what payload, with what throttling)? Flag any notification that would either flood the channel or go silent during a session when it matters.
29. **Guardrails against the trader's worst moments.** Does the RME support hard circuit breakers — max daily loss, max consecutive losses, cooldown after a large loss — that make it visibly harder to keep trading after things have gone wrong? This is advisory, not enforcing, but the *visibility* matters. Flag if this is absent.

#### E. Failure modes and operational realism

30. What happens when the MDP is down, when the FYERS admin daily token refresh fails, when a user's FYERS token expires mid-session, when a WebSocket drops, when a background job misses its window, when SQL Server or MongoDB is briefly unavailable, when Redis evicts, when OAuth providers fail, when Azure App Configuration is unreachable at startup, when the FYERS widget fails to load, when the widget closes without a confirmation response? For each, is the expected behavior documented? Flag missing recovery, retry, idempotency, and backfill semantics.
31. **Non-negotiable guardrails.** Re-read the guardrails in `CLAUDE.md` and verify the documentation cannot be implemented in a way that violates any of them. Flag any ambiguity that would let a developer violate a guardrail while still following the docs.

### Explicitly out of scope

Do not comment on: SEBI regulations, exchange bye-laws, KYC, tax treatment, audit trail for compliance purposes, data localization law, advertising standards, or any other regulatory or compliance topic. Do not comment on short-side workflows, autonomous order placement, or non-Nifty-500 coverage — these are forbidden by the guardrails and are not to be revisited.

### Output format

Produce your review in exactly this structure. Be specific, cite file and section, and do not pad.

- **Executive summary** — 6–10 bullets. Most important findings first. Include at least one bullet each for: Risk Management Engine, trader usability, security, and over-engineering risk.
- **Critical gaps** — issues that would prevent the system from working, cause incorrect financial behavior, or break the advisory-only invariant. For each: location, what is wrong or missing, why it matters to a trader or operator in practice, and the smallest concrete fix or the specific question that must be answered.
- **Risk Management Engine findings** — a dedicated section. Subsections: Robustness, Flexibility, Backtestability, Lifecycle coverage, Transparency, Advisory-only enforcement. List findings under the subsection they belong to.
- **Architectural findings** — including right-sizing, component boundaries, MDP abstraction integrity, and single-instance Worker invariant.
- **Security findings** — application and data security only.
- **Trader-experience findings** — usability, latency, state clarity, Signal trust, notification discipline, and self-protective guardrails.
- **Non-functional gaps that actually matter** — NFRs whose absence would hurt a real trading session or the pilot. Omit decorative NFRs.
- **Logical inconsistencies** — contradictions between or within docs.
- **Underspecified areas** — things mentioned but not defined to a buildable level.
- **Edge cases and failure modes not addressed**.
- **Over-engineering risks** — sections of the docs that add cost or complexity without a matching benefit at the current phase. Name the section and propose the simpler alternative.
- **Questions for the author** — a numbered list of the specific decisions you need clarified before the docs are buildable.

Do not propose rewrites. Propose the smallest change that closes the gap. If a section is fine, do not mention it. If a gap is theoretical and closing it would add complexity without protecting a real trading outcome, do not flag it — or flag it under "Over-engineering risks" if the docs themselves already carry that complexity.