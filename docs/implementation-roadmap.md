# Implementation Roadmap

## Purpose

This roadmap sequences delivery work derived from [requirements-spec.md](./requirements-spec.md).
It must not be used as the place to invent new product requirements.

## Delivery Principle

Ship in thin vertical slices that end in something a user or operator can validate.

## Phase 0: Foundation

- initialize repo structure
- configure local environment, linting, typing, formatting, and tests
- set up Azure DevOps pipelines
- define MongoDB, SQL Server, Redis, and worker runtime patterns
- establish ASP.NET Core backend and .NET worker baselines
- establish Worker Service single-instance deployment invariant per REQ-RME-CONC-006: IaC template sets `workerCount = 1` with no auto-scale rule; Azure DevOps release pipeline includes a preflight check that fails the build if the target Worker App Service plan has any other instance count or any auto-scale rule attached; Worker startup includes the Redis-backed `rme:worker:singleton` lease with hard exit on conflict and graceful degrade (warning only) if Redis is unreachable
- establish OpenTelemetry, OTLP, Serilog, and collector wiring
- establish Azure App Configuration, Key Vault integration, and configuration bootstrap patterns
- wire structured logging and health checks

## Phase 1: Identity and Access

- implement OAuth sign-in
- create user, account, role, and approval models
- implement seeded bootstrap admin behavior per REQ-ROLE-005: no pre-seeded admin user record — the admin user record is created lazily on the admin's first successful OAuth sign-in when the authenticated email matches `SEED_ADMIN_EMAIL`, with the admin role assigned in the same transaction
- add FYERS credential management and token lifecycle handling
- add dirty-token and re-auth-required UX
- build the `sys_config` seeder console application per REQ-CONFIG-011: dedicated .NET console job that reads a source-controlled manifest mirroring the Required Seed Table in `docs/system-config.md`, inserts missing rows only (never overwrites), resolves per-deployment keys from pipeline parameters (fails fast with a list of unresolved keys), and upserts the `platform.seed.version` sentinel with the release identifier on every run; emits a structured report artefact for operator review
- wire the Azure DevOps release pipeline to run migrations and seeding in the mandatory order defined by REQ-CONFIG-010: **FluentMigrator (SQL Server) → Mongo.Migration (MongoDB) → `sys_config` seeder → API/Worker activation**; all three migration/seed steps must succeed before API or Worker deployment slots are activated
- add the sentinel-row startup check to the API and Worker services per REQ-CONFIG-010: on startup, read the `platform.seed.version` row from `sys_config` and fail fast with a clear error if absent — turns a missed seeder step into an actionable deployment failure rather than a silent circular dependency; neither service may self-seed `sys_config` from its startup path
- define the `sys_config` collection schema (stable document shape per REQ-CONFIG-003) and commit the initial manifest backing the seeder; new requirements that introduce a `sys_config` key must add both the spec row and the manifest row in the same change per REQ-CONFIG-009
- build admin management UI for shared runtime `sys_config` settings
- build admin approval flows
- execute the REQ-ORDER-010b CNC sandbox verification gate at the earliest point in Phase 1 at which a FYERS API Connect sandbox account is available (and the earliest subsequent phase otherwise): host a minimal `<fyers-button>` page with `data-product = CNC` for a representative Nifty 500 equity symbol and confirm the widget accepts the submission. Record the verified outcome as an ADR note. A passing result greenlights Phase 7 as planned; a failing result removes Phase 7 from V1 scope per REQ-ORDER-010a, promotes the fallback-wiring build items listed in Phase 6 into the active backlog, and moves platform-native execution assistance to REQ-NEXT-011 for post-V1 investigation.

## Phase 2: Universe and Market Data

**Preconditions (must be confirmed against the FYERS sandbox before Phase 2 market-data code is written):**

- Bulk Quotes rate-limit accounting: verify whether a single Quotes REST request for up to 50 comma-separated symbols consumes one rate-limit hit or `N` rate-limit hits (where `N` equals the number of symbols in the request). The result determines the safe lower bound on `jobs.live_market_scan.poll_interval_seconds` and the overall LMDS capacity model. The verified outcome must be recorded in `docs/operations/fyers-api-budget.md` § LMDS before any LMDS implementation work starts, and the default `sys_config` seed value must be adjusted if the verified accounting invalidates the current 90 s default. Per REQ-MARKET-002c, this verification is FYERS-specific; when the platform migrates to a third-party market data provider the equivalent check must be performed against that provider's published rate-limit rules and the budget model refreshed accordingly.

**Build work:**

- create symbol master and universe state models
- build admin CSV upload with archive and exclusion handling
- implement internal trading calendar and admin management screens
- implement Market Data Provider (MDP) abstraction layer with common interface
- implement FYERS as the initial concrete MDP implementation
- implement TrueData or Global Data Feeds as a second concrete MDP implementation (stub if not yet contracted)
- add admin config for active MDP selection
- build HistoricDataSeed job: operator-triggered, batched, resumable, fetches backward from current date; implement scoped per-symbol table materialisation per REQ-HIST-009a (create `D_{suffix}`, `W_{suffix}`, `M_{suffix}` idempotently with `IF NOT EXISTS` semantics inside a per-symbol transaction before writing candles; shared DDL template keyed by timeframe prefix; each creation event logged to `job_runs`)
- build post-market DataSync job: daily, fetches current day minus 10 sessions through current session as recovery buffer
- build chart-data endpoints and live quote flows routing through provider abstraction
- define SQL Server historical schema and sync patterns

## Phase 3: Strategy Research

- model Signal Subscription definitions, versions, and persisted parameter sets
- implement SQL Server historical query layer
- implement shared timeframe aggregation
- build backtest engine and result storage
- deliver Signal Builder and backtest result screens

## Phase 4: Live Operations

- implement DataSync (DS) EOD job and success-marker handling
- implement EOD Signal Runner (EODSR)
- implement notification store in MongoDB as the primary record of all alerts and advisories
- implement async Telegram delivery worker with retry and back-off; mark delivery status on each record
- add Telegram bot provisioning and user deep-link subscription flow
- add Telegram deep links and brief portfolio impact summary into signal notifications
- build portal notification feed with filtering, unread indicators, and deep links to chart pages
- flag failed Telegram deliveries visibly in the portal feed
- honor universe archive and scan-exclusion rules in live workflows

## Phase 5: Portfolio Analytics

**Entry precondition:** The corporate-action detection threshold calibration note (`docs/adr/0004-calibration-note-YYYYMMDD.md`) must be committed before Phase 5 implementation begins. This calibration governs which positions enter the `SuspendedForCorporateAction` state (EC-3 / REQ-PLC-00x) and the threshold cannot be changed without re-verifying the detection code. See ADR-0004 for calibration owner, sample window, and deliverable form.

- sync trades, holdings, positions, and orders from FYERS
- build immutable trade ledger
- support first-time backfill and incremental sync
- add reconciliation and mismatch detection
- support audited manual adjustments tied to holdings
- deliver holdings, PnL, and reconciliation views

## Phase 6: Risk Management Engine

- implement the RME as a standalone, strategy-independent module with abstract plugin interfaces
- implement R-based risk unit calculations
- implement position lifecycle state machine (PendingEntry through Closed and Suspended)
- implement pluggable position sizing models: Fixed Percentage Risk Per Trade (default), ATR-based, Portfolio Heat-based, and Drawdown-adjusted
- implement pluggable stop loss mechanisms: Fixed, ATR, Trailing (percentage), Trailing (ATR), Swing Low, and Break-Even
- implement RME profile concept: unified configuration set at position entry, persisted with position record, immutable for lifecycle duration
- implement portfolio heat calculation and enforcement
- implement account-level drawdown tracking from equity high-water mark
- implement graduated drawdown response rules and drawdown mode behaviour
- implement pyramiding and scaling advisory logic with add and reduce level computation
- implement z-score overlay as an add-on advisory signal
- implement exit priority ordering when multiple conditions trigger simultaneously
- implement gap risk and circuit limit detection and advisory surfacing
- implement pre-trade portfolio impact assessment: projected heat, sector exposure, cash reserve, correlation context, and worst-case stop scenario
- implement execution reality modelling for backtesting (slippage, commission, gap fills)
- implement per-trade and portfolio-level risk metrics (MAE, MFE, R multiples, Sharpe, Sortino, etc.)
- implement automated RME profile optimisation backtests: run symbol entry signals through multiple profile configurations, compare outcomes, store and surface ranked recommendations per symbol
- wire RME into strategy backtest engine and live scan workflows using one shared implementation
- surface full RME advisory panel on chart page: entry rationale, stop level, add and reduce levels, current R multiple, portfolio impact breakdown
- surface brief portfolio impact summary in Telegram entry signal notifications
- surface RME advisory state and portfolio health indicators when user browses positions manually
- implement admin global kill switch for platform-wide signal suspension
- implement per-user and per-strategy enable and disable controls
- build user dashboard: portfolio summary, XIRR, period performance (today, rolling 5/15/30 sessions, WTD, MTD), sector breakdown, positions summary with RME advisory state, and RME portfolio health strip

**Conditional Phase 6 build items — activated only if REQ-ORDER-010b CNC verification fails and the REQ-ORDER-010a fallback is in effect:**

- wire the FYERS-handoff UX into every surface that would otherwise have invoked the Phase 7 Phase 1 modal: chart page action buttons for Entry, Add, Reduce, and Exit; positions summary action controls; action affordances for externally-opened holdings per REQ-ORDER-019. Each action button must present the REQ-ORDER-017 / 017a handoff message ("execution must be completed in the FYERS app or web platform directly") together with a symbol-contextualised deep link to FYERS where feasible; no Phase 1 modal is opened and no `intent_ledger` record is written
- ensure the RME advisory, Telegram notification, portfolio analytics, and dashboard flows remain functionally unchanged in fallback mode; the fallback only replaces the in-portal submission path
- add a visible product-identity note on the chart page and dashboard clarifying that V1 ships without platform-native execution assistance per REQ-ORDER-010a, with execution assistance tracked as REQ-NEXT-011 for a future phase

## Phase 7: Execution Assistance

**Entry condition:** Phase 7 is entered only if the REQ-ORDER-010b sandbox verification (scheduled in Phase 1) succeeded. If the verification failed, Phase 7 is skipped for V1 per REQ-ORDER-010a, the conditional Phase 6 fallback-wiring build items are delivered instead, and platform-native execution assistance is moved to REQ-NEXT-011.

**Preconditions (must be confirmed against the FYERS sandbox before build starts):**

- `CNC` is an accepted `<fyers-button>` `data-product` value for a representative Nifty 500 equity symbol (REQ-ORDER-010). This has already been verified in Phase 1 per REQ-ORDER-010b; Phase 7 build proceeds only if that verification passed. The Phase 7 contract test suite re-exercises the check (test 6) as a regression gate against SDK revisions.
- Exact `status` enum values for the `finished` callback and the payload shape of `request_token` (whether it is the order ID directly or must be exchanged for one).
- Whether the SDK exposes a per-button correlation or tag attribute the platform can use to disambiguate multiple near-simultaneous orders from the same user.

**Build work:**

- embed FYERS API Connect JS SDK (`fyers-lib.js`) in the Next.js portal; pin the SDK URL in platform configuration and wire a deployment-time hash check so SDK changes trigger the contract test suite
- implement pre-flight check API endpoint: FYERS token validity, kill-switch state, drawdown block state, duplicate-in-flight detection
- build Phase 1 platform confirmation modal for Entry, Add, Reduce, and Exit actions: RME advisory state, recommended quantity, stop level, post-trade portfolio heat, Market/Limit order type selection with real-time value recalculation (Limit defaults to RME-suggested price), CNC product type enforced
- implement non-blocking warning layer in Phase 1 modal: price-distance from RME level, heat-approaching-limit, drawdown-adjusted sizing
- implement the signed order payload endpoint (REQ-ORDER-009b): server-side validation of every attribute, HMAC signing bound to session + action + single-use nonce; frontend requests a fresh signed payload on every Phase 1 recalculation and uses it verbatim to populate `<fyers-button>` `data-*` attributes
- provision the `intent_ledger` MongoDB collection per `docs/data-management.md` with the specified indexes (compound `{ user_id, status, created_at }`, unique `nonce`)
- implement the pre-widget intent write (REQ-ORDER-014a) using the chosen capture strategy (capture-phase click handler or two-step confirm); exercise the choice in the contract test suite to prove no widget activation path bypasses the intent write
- on Phase 1 confirm, pre-populate FYERS API Connect widget attributes from the signed payload and invoke the widget; FYERS-hosted pop-up handles auth, exchange checks, and final submission
- register the global `finished` callback handler (REQ-ORDER-015): on success mark intent `matched` with `match_source = "callback"`, record `request_token` and order ID if returned, surface a portal-only "submitted to FYERS" confirmation banner (no Telegram, no feed entry — see REQ-ORDER-015f), write audit log entry; on failure mark intent `submission_failed`, surface FYERS error, write audit log entry; wrap the handler in a top-level exception boundary. **The callback writes only to `intent_ledger`; it must never advance the position lifecycle — `PendingEntry → Open` is driven exclusively by LADS-confirmed fill evidence per REQ-ORDER-015 and the build item below.**
- extend LADS with the intent-reconciliation safety net (REQ-ORDER-015c): on every cycle match observed FYERS orders against `pending` intents; close callback-matched intents with `reconciled_at`; promote `pending → matched` with `match_source = "lads_reconciliation"` where callback never arrived; transition stale intents to `unresolved`; raise `orphan_ack` advisories for observed orders with no matching intent
- queue RME state refresh on next portfolio sync following a confirmed fill (PendingEntry → Open is driven by LADS-confirmed fill evidence, not by the callback alone — this is the single authoritative driver of the position lifecycle transition)
- implement the awaiting-confirmation portal state (REQ-ORDER-015e) on the chart page, positions summary, and Phase 1 modal block: display submission timestamp, most recent LADS poll timestamp, and LADS-cycle-bounded wait context; clear on LADS-driven `PendingEntry → Open` or on the REQ-ORDER-015c `unresolved` / `orphan_ack` advisory replacing it
- implement LADS-gated "entry filled" and "entry partially filled" user notifications (REQ-ORDER-015f): Telegram dispatch and portal feed entry fire on LADS-confirmed fill evidence only; callback success produces no Telegram and no feed entry
- emit callback-reliability OpenTelemetry metrics (REQ-ORDER-015d) and wire the admin alert that fires when the LADS-only match ratio exceeds `orders.callback_failure_alert_ratio`

**Contract test suite (run against the FYERS sandbox before release and on every detected SDK URL change):**

1. successful submission invokes the `finished` callback with a recognised success status and a `request_token`
2. successful submission produces an intent matching a LADS-observed order within the timeout window, with `match_source = "callback"` when the callback fired and `match_source = "lads_reconciliation"` when it did not
3. an intent created without a subsequent order submission times out to `unresolved`
4. a LADS-observed order with no matching intent surfaces as an `orphan_ack` advisory
5. an intentionally-thrown exception inside the callback handler does not prevent reconciliation from completing the intent
6. `data-product = CNC` is accepted by the widget for a representative Nifty 500 symbol
7. signed-payload nonce replay is refused; mutated `data-*` values between signing and click are refused
8. a `finished` callback with a success status does not advance the corresponding position from `PendingEntry` to `Open`, and the position only transitions after the next LADS cycle observes the fill (REQ-ORDER-015 / REQ-ORDER-015e)
9. a `finished` callback with a success status does not produce a Telegram entry-fill notification or a portal feed entry, and both are produced only on LADS-confirmed fill evidence (REQ-ORDER-015f)
10. while the intent is `matched` but the position remains `PendingEntry`, the Phase 1 modal for the same symbol and action type is blocked with the awaiting-fill-confirmation wording defined in REQ-ORDER-015e

## Phase 8: Admin Operations and Hardening

- add admin job monitoring and manual retry controls
- add alerting, kill-switches, and operational safeguards
- harden retries, dead-letter handling, and reconciliation jobs
- document incident and recovery workflows

## Phase 9: Corporate Actions and Data Continuity

- add splits, bonuses, dividends, and symbol continuity handling
- decide whether provider data is sufficient or a dedicated source is required
- apply continuity rules across charts, backtests, and holdings analytics

## Ongoing Rules

- start each feature from domain model and interface design
- keep roadmap phases aligned with the canonical requirements spec
- update architecture and standards docs only when their concerns change
