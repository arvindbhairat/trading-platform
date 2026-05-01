# ADR-0004 — Corporate Action Discontinuity Detection Threshold

## Status

**Accepted — 2026-05-01 — calibration note committed at `docs/adr/0004-calibration-note-20260501.md`**

The calibration note confirms the 50% default as the correct threshold for Phase A operations. See the calibration note for full analysis of Nifty 500 corporate actions from the 2022–2023 sample window, false-positive/false-negative estimates, and monitoring plan.

---

## Context

Until full corporate actions handling is available (Phase 9), the platform detects *potential* corporate action events by comparing FYERS-reported holding state against the internal FIFO trade ledger (REQ-PORT-016). A discontinuity is flagged when the implied average cost per unit from FYERS deviates from the FIFO-derived average buy price by more than a configurable percentage threshold.

The initial default threshold is **50%**, chosen as a rough first-principles estimate. This ADR records the known limitations of that value and the calibration work required before Phase 5 implementation begins.

---

## The Problem with 50%

NSE corporate actions produce predictable cost-per-unit deltas. Mapping common action types to expected FYERS vs. FIFO divergence:

| Corporate Action | Typical Ratio | Expected Cost Delta | 50% threshold triggers? |
|---|---|---|---|
| 2:1 stock split (company doubles shares) | 2× quantity, ½ price | ~50% | At the boundary — may or may not trigger depending on rounding |
| 3:1 stock split | 3× quantity, ⅓ price | ~67% | Yes |
| 10:1 stock split | 10× quantity, ⅒ price | ~90% | Yes |
| 1:1 bonus issue (1 free share per share held) | 2× quantity, ½ avg cost | ~50% | At the boundary |
| 3:2 bonus issue (1 free share per 2 held) | 1.5× quantity, ⅔ avg cost | ~33% | **No — missed** |
| 1:2 rights issue (1 new share per 2 held at discount) | Varies by price | 10–30% typically | **No — likely missed** |
| Stock consolidation / reverse split (1:10) | 0.1× quantity, 10× price | ~90% | Yes — but in the wrong direction; quantity mismatch (condition a) catches it first |
| Dividend (cash) | No quantity or price change | 0% | No — correct, cash dividends don't change FIFO state |
| Demerger / spin-off | Complex | Varies widely | Caught by quantity mismatch (condition a) if shares are new; may miss value-only demergers |

### Key findings

1. **2:1 splits and 1:1 bonuses sit exactly at 50%**, making the trigger boundary-sensitive. Whether they trip the threshold depends on floating-point rounding and the precision of FYERS's reported holding value. This is unreliable.

2. **3:2 bonus issues (33%) and most rights issues fall below 50% and are missed by condition (b).** They would still be caught by condition (a) (quantity mismatch) if FYERS correctly reports the updated quantity — but FYERS's quantity reporting for rights and bonus may lag by a session.

3. **The threshold does not help with value-only corporate actions** (mergers at a price ratio without a quantity change). These are rare in Nifty 500 but not impossible.

4. **Normal mark-to-market drift will never approach 50%** on a daily basis for Nifty 500 stocks without a circuit-limit event. The threshold is safe from false positives due to price movement alone, but an operator must verify this holds across recent volatility data.

---

## Decision

**Keep 50% as the seeded default, make it configurable, and mandate calibration before Phase 5.**

Rationale:
- The threshold is stored in `sys_config` under `risk.corporate_action.avg_cost_delta_threshold_pct` and is admin-editable at any time.
- A false negative (missed detection) is less harmful than a false positive (spurious flag): a missed flag means the platform does not suspend RME monitoring for a position that may have incorrect stop levels; a false positive suspends monitoring unnecessarily and generates a confusing user warning. Erring toward fewer false positives at 50% is therefore the safer default in Phase A where the admin can manually investigate.
- Condition (a) (quantity mismatch) catches the majority of corporate actions independently of the threshold, since most actions change the share count.

**Calibration required before Phase 5 implementation:**

**Owner:** Platform Engineering Lead (or the developer responsible for the portfolio analytics phase). The calibration deliverable is a personal responsibility of this role; it may not be deferred to a later phase or to a third party without the explicit sign-off of the project lead.

**Sample window:** NSE corporate actions over a 24-month period from 2022-01-01 to 2023-12-31 (or the most recent 24 calendar months available at the time calibration begins). The sample must be sourced from NSE's official corporate actions data on nseindia.com, filtered to Nifty 500 constituents during the sample window. This is a concrete, reproducible dataset, not a forward-looking or estimated sample.

**Deliverable:** A calibration note committed to `docs/adr/0004-calibration-note-YYYYMMDD.md` (where YYYYMMDD is the date the calibration analysis was completed). The note must record: the sample source and date range used; the action types analysed and their count; the threshold value recommended by the analysis; the false-positive and false-negative estimates at that value; and the recommended updated `sys_config` seed value for `risk.corporate_action.avg_cost_delta_threshold_pct`. If the analysis confirms 50% is the optimal default, the note must state that explicitly.

**Phase 5 entry precondition:** Phase 5 (portfolio analytics) implementation must not begin until the calibration note is committed to `docs/adr/` and its `docs/implementation-roadmap.md` entry is updated to reflect the calibrated threshold. This is a hard gate — not a "nice to have" — because the threshold governs which positions enter `SuspendedForCorporateAction` (EC-3) and incorrect thresholds produce either missed suspensions or unnecessary suspensions on live user positions.

Before the corporate action detection code is written, the implementer must:

1. Obtain a sample of NSE corporate actions from the past 24 months for Nifty 500 constituents per the sample window above.
2. For each action, compute the expected cost-per-unit delta if FYERS's reported holding value reflects the post-action price and the FIFO ledger reflects the pre-action average buy.
3. Determine: (a) at what threshold all 2:1 splits and 1:1 bonuses are reliably caught; (b) whether 3:2 bonus issues are material enough in Nifty 500 to warrant lowering the threshold; (c) the false-positive rate at various threshold values given historical Nifty 500 daily price moves.
4. Update the `sys_config` seed value in `docs/system-config.md` with the calibrated threshold.
5. Commit the calibration note and update this ADR status to **Accepted**, recording the calibrated value, the sample used, and the resulting false-positive and false-negative estimates.

---

## Consequences

- The 50% default is functional but may miss 3:2 bonus issues and rights issues that do not produce a quantity mismatch. Users in Phase A should be informed (via the platform disclaimer) that corporate action detection is best-effort during this phase.
- The calibration step is gated by Phase 5, not by Phase A launch. Phase A is acceptable with the 50% default because: (a) the admin is the only user initially; (b) the admin can manually inspect any holding showing unexpected divergence; and (c) condition (a) catches the majority of corporate actions anyway.
- If calibration reveals a better threshold (e.g., 30%), updating `sys_config` takes effect immediately without a code change.
- This detection mechanism is a temporary proxy. The correct long-term solution — ingesting NSE corporate action announcements and applying them automatically — is deferred to Phase 9 (REQ-NEXT-010).
