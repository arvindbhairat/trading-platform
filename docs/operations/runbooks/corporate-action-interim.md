# Runbook: Interim Corporate Action Manual Intervention

**Applies to:** Phase A and Phase B (until Phase 9 CA automation is shipped)
**Owner:** Admin / Platform Operator
**Triggered by:** REQ-PLC-010a
**See also:** ADR-0004, REQ-PLC-010, REQ-ADMIN-016(c/d), datasync-failure-recovery.md

---

## Purpose

Full corporate action ingestion and automated price-level re-anchoring is deferred to Phase 9
(REQ-NEXT-010). Until then, every CA that affects a live position must be handled through this
manual procedure. Three triggering scenarios exist:

| Scenario | How detected | Starting suspension source |
|---|---|---|
| **Detected** | Platform REQ-PORT-016 discontinuity check fires | `CORPORATE_ACTION` (automatic via REQ-PLC-010) |
| **Undetected** | Admin learns of CA from NSE announcement or broker before platform detects it | `ADMIN` (manual freeze, this runbook step 1b) |
| **Operator-reported** | Any external source (user complaint, broker alert, NSE circular) | `ADMIN` (manual freeze, this runbook step 1b) |

---

## Step 1 — Freeze the Position

### 1a. If the platform has already auto-detected the CA (scenario: Detected)

The position is already in `Suspended (corporate_action_detected)` state. An `rme_incidents`
record with `incident_type: corporate_action_suspension` has been written. Skip to Step 2.

### 1b. If the platform has NOT yet detected the CA (scenarios: Undetected / Operator-reported)

The admin must manually freeze the position **before** any re-anchoring work to prevent the RME
from applying erroneous level updates during the detection lag window.

1. Navigate to **Admin → Positions → [affected user] → [affected symbol]**.
2. Verify the position is in `Open` or `PendingEntry` state (not yet auto-suspended).
3. Complete step-up re-authentication (REQ-SEC-011).
4. Use **"Manually suspend position"** (source `ADMIN`, REQ-ADMIN-016(d)).
5. Enter a justification identifying the CA type, announcement source, and effective date (e.g.,
   `"3:2 bonus issue effective 2026-04-15, per NSE circular XYZ. Platform detection not yet triggered."`).
6. Confirm the position transitions to `Suspended (ADMIN)` and an `audit_events` record is written.

> **Important:** Do not skip this freeze step. A position left in `Open` state while you gather
> CA data will continue to receive RME level updates based on the pre-adjustment cost structure,
> which will produce incorrect stop and add level advisories.

---

## Step 2 — Gather Corporate Action Data

Collect the following for the CA before proceeding:

| Field | Source |
|---|---|
| CA type (split, bonus, rights, dividend, demerger) | NSE corporate action circular |
| Ex-date (the trading session on which the adjustment takes effect) | NSE / FYERS holdings |
| Ratio or adjustment factor | NSE circular (e.g., 3:2 bonus = 1 bonus share per 2 held) |
| Pre-CA quantity held (per platform trade ledger) | Admin → Portfolio → Trade Ledger for user |
| Post-CA quantity expected | Computed from ratio × pre-CA quantity |
| FYERS-reported post-CA quantity (after settlement) | FYERS Holdings page or LADS sync |
| Pre-CA FIFO average buy price | Admin → Portfolio → Position Detail |
| Post-CA adjusted average buy price | Computed: pre-CA avg price ÷ new-to-old quantity ratio |
| Implied stop level adjustment factor | Same ratio as price (e.g., ÷ 1.5 for 3:2 bonus) |

**Common adjustment formulas:**

| CA Type | Quantity multiplier | Price multiplier | Stop multiplier |
|---|---|---|---|
| 2:1 split | ×2 | ×0.5 | ×0.5 |
| 3:1 split | ×3 | ×0.333 | ×0.333 |
| 1:1 bonus | ×2 | ×0.5 | ×0.5 |
| 3:2 bonus | ×1.5 | ×0.667 | ×0.667 |
| Rights issue | Depends on rights price and ratio | Computed from blended cost | Adjusted proportionally |
| Reverse split 1:10 | ×0.1 | ×10 | ×10 |

---

## Step 3 — Verify FYERS Settlement

Before re-anchoring, confirm FYERS has settled the CA in the user's demat account:

1. Trigger a manual LADS sync for the user: **Admin → Users → [user] → "Run account sync now"**.
2. Compare the FYERS-reported quantity in the admin portal against the expected post-CA quantity
   computed in Step 2.
3. If FYERS quantity still shows pre-CA: the settlement has not propagated yet. **Do not proceed
   to re-anchoring.** Retry LADS sync the following session or await FYERS confirmation.
4. If FYERS quantity matches post-CA: proceed to Step 4.

---

## Step 4 — Re-anchor the Position

### For CORPORATE_ACTION-sourced suspensions (auto-detected, scenario 1a)

1. Navigate to **Admin → Positions → [user] → [symbol] → incident detail**.
2. The incident resolution screen (REQ-ADMIN-016(c)) presents the "Re-anchor and release" action.
3. Complete step-up re-authentication.
4. Enter the post-CA adjusted values:
   - New quantity (post-CA)
   - New weighted average entry price (post-CA)
   - Adjusted stop loss level (original stop × price multiplier from Step 2 table)
   - Adjusted trailing stop level if active (same multiplier)
   - Adjusted add and reduce advisory levels if configured (same multiplier)
5. Confirm the re-anchor. The platform writes the adjustment ratio, original values, new values,
   and the admin identity to the position's audit history.
6. Verify the `rme_incidents` record is marked resolved and the position returns to `Open`.

### For ADMIN-sourced suspensions (manual freeze, scenarios 1b/1c)

1. Navigate to **Admin → Positions → [user] → [symbol]**.
2. Manually update the position's cost basis and level prices through the manual adjustment
   flow (REQ-PORT-025 / REQ-PORT-026), applying the same computed values as above. Each field
   update must carry an adjustment reason referencing this runbook and the CA details.
3. Once all field adjustments are complete, use **"Release suspension"** (REQ-ADMIN-016(d)) with
   a justification note.
4. Complete step-up re-authentication for the release.
5. Verify the position returns to `Open` and LMDS resumes level monitoring.

---

## Step 5 — Validate and Notify

1. Confirm the user receives the `corporate_action_warning` notification (REQ-NOTIFY-006) if not
   already delivered by the auto-detection path.
2. Confirm LMDS resumes stop-level monitoring for the position (check System Health widget).
3. Confirm RME recalculates portfolio heat and exposure with the new position size (next LMDS poll
   or manual RME recalc trigger).
4. If the re-anchoring materially changes the user's portfolio heat — for example, a 3:2 bonus
   doubles the position notional — check whether the revised position now breaches any heat or
   sector exposure limits (REQ-HEAT-001, REQ-HEAT-003) and send an advisory notification if so.
5. Record a note in the position's audit trail summarising the CA type, ex-date, adjustment
   factors applied, and the session on which resolution was completed.

---

## Edge Cases

**CA not settled by FYERS after 3 sessions:** Raise a support ticket with the broker. Keep the
position suspended in the interim. Do not release the suspension until FYERS settlement is
confirmed.

**Multiple open positions in the same symbol (pyramiding tranches):** Apply the adjustment to
each tranche independently. All tranche stops must satisfy the pyramid floor invariant (REQ-PYR-011)
after adjustment — verify the effective combined stop has not been inadvertently lowered.

**CA affects an archived symbol:** Proceed through the same steps. Archived symbols continue to
receive RME monitoring for open positions (REQ-UNIV-007). The CA re-anchor applies regardless of
archive state.

**ADR-0004 threshold missed and platform never auto-detected:** After completing this runbook,
record the CA type, date, and detection outcome in the ADR-0004 calibration note
(`docs/adr/0004-calibration-note-YYYYMMDD.md`). If the pattern of misses justifies a threshold
adjustment, update the `sys_config` key `risk.corporate_action.avg_cost_delta_threshold_pct`
through the admin portal and record the change in `audit_events`.

**Rights issue with fractional entitlement:** Calculate the blended post-rights average cost
(pre-rights qty × pre-rights avg price + rights qty × rights subscription price) ÷ total qty.
Use this blended figure as the new average entry price. Adjust the stop proportionally based on
the original stop distance in R-terms, not as a simple price multiplier.

---

## Post-Incident Record

After every CA intervention, add an entry to the ADR-0004 log section (or a separate
`docs/adr/0004-calibration-note-YYYYMMDD.md` file) noting:
- Symbol, CA type, ex-date
- Whether platform auto-detected it (scenario) and if not, why
- Adjustment factors applied
- Time from CA ex-date to position resolution

This log feeds the Phase 5 calibration exercise required by ADR-0004.
