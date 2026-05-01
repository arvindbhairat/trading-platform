# FYERS API Daily Budget Model

**Version:** v2 — Phase A baseline (verified 2026-05-01)
**Requirement basis:** `REQ-RATE-011` mandates this document exists; `REQ-RATE-012` requires the model be recalculated whenever its input sys_config values change.

---

## Verified Rate-Limit Accounting (P3-T1)

Verification performed 2026-05-01 against FYERS published documentation and community sources.

### Check 1 — Bulk Quotes rate-limit counting
**Result: Confirmed as 1 API call per HTTP request (not per symbol).**

A `fyers.quotes()` call with up to 50 comma-separated symbols counts as a single API call against the daily rate limit. This is consistent with standard REST API semantics (one HTTP request = one consumption unit) and with the FYERS community discourse, where the question has been raised but the understood behaviour is one hit per request. The FYERS V3 announcement's framing of a "10x boost" (10k → 100k) is consistent only with per-request counting — per-symbol counting would already yield an effective 500k symbol-quotes/day ceiling under the old limit.

**Budget impact:** The existing LMDS budget figure of **1,250 calls/day** (assuming bulk quotes) is correct. The per-symbol fallback figure of 62,500 calls/day is retained as a defensive bound for the no-bulk-path scenario.

### Check 2 — Daily limit value
**Result: Confirmed as 100,000 requests/day (FYERS API V3).**

The FYERS API V3 announcement published at `fyers.in/community/blogs/...` states the daily rate limit was increased to 1 lakh (100,000) requests per day — a 10x increase from the previous 10,000/day limit. Additional sub-day limits reported in community sources: ~10 requests/second and ~200 requests/minute.

**Budget impact:** The seeded `integrations.fyers.rate_limit.per_day` value of 100,000 is correct for V3. The existing budget rows remain valid without recomputation. The 90-second `jobs.live_market_scan.poll_interval_seconds` default also remains valid — even under the per-symbol fallback, LMDS consumes 62.5% of daily capacity, staying under the 80% budget threshold.

### Implication for sys_config defaults
No adjustment is required. Both the `integrations.fyers.rate_limit.per_day` (100,000) and `jobs.live_market_scan.poll_interval_seconds` (90) defaults are validated against current FYERS published limits. The per-second limit of ~10 req/s and per-minute limit of ~200 req/min are noted for the throttling layer implementation (P3-T9) but do not affect the budget model.

Per REQ-MARKET-002c, the same two checks must be performed against the new provider's documented rate-limit rules whenever the platform switches to a third-party market data provider.

---

## Purpose

This document quantifies expected daily FYERS API call consumption from every platform component that uses the shared admin FYERS token. It exists so that sys_config changes affecting any input can be evaluated against the configured daily limit before they're saved, and so the admin portal has a concrete model to project against.

Per-user FYERS API calls (Portal Live Data and Live Account Data Scan, which use each user's own token) are modelled separately in the final section; they have their own independent per-user rate limits with FYERS and are not drawn from the shared pool.

## Inputs

All values below are sourced from `sys_config` at the time of modelling. When any one changes, the model must be recomputed and this document updated per `REQ-RATE-011`.

| Input | Source key | Phase A default |
|---|---|---|
| Daily FYERS call limit (shared pool) | `integrations.fyers.rate_limit.per_day` | 100,000 |
| LMDS poll interval (seconds) | `jobs.live_market_scan.poll_interval_seconds` | 90 |
| Intraday account sync interval (minutes) | `jobs.account_sync.intraday_interval_minutes` | 15 |
| Max concurrent open positions per user | `risk.default.max_concurrent_positions` | 20 |
| Approved user ceiling (Phase A) | `operations.phase_a.tester_ceiling` | 30 |
| Nifty 500 universe size | Symbol master count | ~500 |
| NSE market hours per session | Trading calendar | 6.25 h = 22,500 s = 375 min |
| Typical unique-symbol dedup factor for LMDS | Empirical | ~2.5× (600 position-pairs → ~250 unique symbols) |
| FYERS bulk quote symbols per call | API capability | 50 |

## Per-Job Math

### 1. Live Market Data Scan (LMDS)

LMDS polls every open and pending-entry position symbol during market hours, using the shared admin FYERS token. Symbols watched by multiple users collapse to a single API call per cycle thanks to request deduplication at the data access layer.

- Worst-case unique symbols: min(30 users × 20 positions, 500 Nifty 500) = 500
- Realistic unique symbols after dedup at Phase A scale: ~250
- Polls per market session: 22,500 s ÷ 90 s = **250 polls**
- Calls per poll assuming FYERS bulk quote endpoint (50 symbols/call):
  - 250 symbols ÷ 50 = **5 calls per cycle**
- Calls per poll assuming per-symbol (no bulk):
  - 250 calls per cycle
- **LMDS daily budget with bulk:** 5 × 250 = **1,250 calls/day**
- **LMDS daily budget without bulk:** 250 × 250 = **62,500 calls/day**

### 2. DataSync (DS)

Runs once post-market. For each active Nifty 500 symbol it fetches the current session's daily candle. Weekly and monthly candles are derived from daily data in SQL Server — no additional API calls. REQ-MARKET-009 requires a 10-session recovery window, but in the steady-state case only the current day's candle is fetched per symbol because earlier sessions are already present.

- Symbols polled: ~500 (Nifty 500 plus archived symbols still receiving data per REQ-UNIV-008)
- Calls per symbol: 1 (a single historical range request)
- **DataSync daily budget:** ~500 calls/day

### 3. HistoricDataSeed (HDS)

Operator-triggered, not a scheduled job. Does not contribute to the daily baseline. Called out for completeness because operator-triggered runs during market hours consume the same pool.

- Full universe seed (one-off): FYERS allows ~100 candles per range call; 5 years ≈ 1,250 sessions ÷ 100 = ~13 calls per symbol. 500 symbols × 13 = 6,500 calls for a full seed.
- Typical quarterly rebalance seed (5–10 new symbols): ~130 calls.
- **HDS contribution to steady-state daily budget:** 0. Reserve 1,000–7,000 headroom on full-seed days.

### 4. EOD Signal Runner (EODSR)

EODSR reads directly from SQL Server; it does not call the FYERS market data API.

- **EODSR daily budget:** 0 calls.

### 5. Admin miscellaneous

Admin portal operations — manual reseeds, provider health pings, token validation checks, FYERS app verification — during a normal day. Sized loosely for headroom; no scheduled scheduled baseline.

- **Admin misc daily budget:** reserve 500 calls/day.

## Total Shared-Pool Budget — Steady State

Assumes Phase A defaults as listed in the Inputs table and steady-state operation (no full HDS seed in progress).

| Consumer | Calls/day (bulk LMDS) | Calls/day (no bulk LMDS) |
|---|---:|---:|
| LMDS | 1,250 | 62,500 |
| DataSync | 500 | 500 |
| HistoricDataSeed | 0 | 0 |
| EOD Signal Runner | 0 | 0 |
| Admin misc | 500 | 500 |
| **Total** | **2,250** | **63,500** |
| **% of daily limit (100,000)** | **2.3%** | **63.5%** |
| **Against 80% budget threshold** | Safe | Safe |

**Read:** if the FYERS bulk quote endpoint is used, Phase A consumes roughly 2% of the daily limit. Even if the platform were to fall back to per-symbol quote fetching, the 90-second LMDS interval keeps total consumption under the 80% budget threshold.

## Sensitivity — What Breaks the Budget

The following hypothetical changes illustrate why specific sys_config values matter to the budget. All scenarios assume per-symbol (no-bulk) LMDS, which is the defensive view.

| Scenario | Changed input | LMDS calls/day | Total daily | % of limit | Verdict |
|---|---|---:|---:|---:|---|
| Phase A baseline | — | 62,500 | 63,500 | 63.5% | Safe |
| Tighten LMDS to 60s | `poll_interval_seconds: 60` | 93,750 | 94,750 | 94.8% | Breach (>80%) — blocked per REQ-RATE-012 |
| Tighten LMDS to 30s | `poll_interval_seconds: 30` | 187,500 | 188,500 | 188% | Hard breach — over the daily limit itself |
| Grow to Phase B scale | 60 users × 20 positions, dedup ~400 unique | 100,000 | 101,000 | 101% | Requires bulk LMDS to fit, or poll interval ≥ 120s |
| Full HDS seed in progress | Add 6,500 one-off calls | 62,500 + 6,500 | 70,000 | 70% | Safe |
| Raise per_day limit | `rate_limit.per_day: 200,000` | 62,500 | 63,500 | 31.8% | Safe but requires admin justification under REQ-RATE-012 |

The key takeaway: the **90-second LMDS default in `jobs.live_market_scan.poll_interval_seconds`** is load-bearing for fitting Phase A inside the FYERS daily limit when no bulk endpoint is assumed. If that value is tightened without first verifying bulk quote support is wired up at the MDP layer, the budget will breach.

## Per-User Token Budget (For Reference)

These calls do not draw from the shared admin pool. Each user has their own 100,000-per-day FYERS limit.

### Live Account Data Scan (LADS) — `REQ-PORT-019`

- Sync interval: 15 min during market hours
- Cycles per session: 375 / 15 = 25
- API calls per cycle: ~4 (orders, trades, positions, holdings)
- **Per user: 100 calls/day** (0.1% of the user's own limit)

### Portal Live Data (PLD) — chart and watchlist price fetches

- Variable; upper-bound estimate for an active user on chart and watchlist: ~500 requests/day
- **Per user: ~500 calls/day** (0.5% of the user's own limit)

**Both are comfortably inside the per-user limit** at Phase A densities and remain so into Phase C unless any single user opens charts at pathological frequency.

## Budget Projection Method and Reset Boundary

REQ-RATE-012 requires the admin portal to project whether a proposed sys_config change will breach the 80% daily budget threshold. The projection uses the following method:

**Projection = (calls already consumed today) + (projected calls for remainder of day at new settings)**

- "Already consumed today" is read from the rate-management telemetry store, keyed by the current UTC calendar day.
- "Remainder of day at new settings" is computed from the new config values applied against the remaining market-hours window in the current UTC day.
- The reset boundary is **UTC midnight** — the daily call counter resets at 00:00 UTC. This is not the same as IST market close (approximately 10:30 UTC) or the FYERS vendor reset time (to be verified and recorded here when confirmed against FYERS documentation). The UTC midnight reset boundary is recorded in `sys_config` under the key `integrations.fyers.rate_limit.reset_boundary` (default `utc_midnight`). Future provider adapters that reset at a different boundary (e.g., IST midnight, IST market open) must override this key so the projection math uses the correct window.

This projection method avoids penalising mid-session saves for consumption that has already occurred and cannot be undone, which a "full day at new settings" projection would do incorrectly.

## Review Triggers

This document must be reviewed and, if needed, revised when any of the following happens:

1. Any of the Inputs table values changes in `sys_config`.
2. The FYERS bulk quote endpoint's symbols-per-call limit changes, or bulk support is enabled or disabled at the MDP layer.
3. The platform transitions from Phase A to Phase B or C, or the approved user ceiling is raised.
4. A new background job is added that calls through the shared admin token.
5. Observed previous-day consumption diverges from this model's projection by more than 20%.

Every revision must bump the document version in the frontmatter and record the revision event in `audit_events`, per REQ-RATE-011.

## Change Log

- **v2** — 2026-05-01. P3-T1 verification: confirmed bulk quotes count as 1 API call per request (not per symbol) and daily limit of 100,000/day is correct for FYERS API V3. No budget model changes required. Added per-second (~10/s) and per-minute (~200/min) limits as throttling-layer context.
- **v1** — Initial Phase A model at 30 users, 90-second LMDS interval, bulk quote assumed present at the MDP layer. Establishes the 80% budget threshold baseline that REQ-RATE-012 enforces on sys_config saves.
