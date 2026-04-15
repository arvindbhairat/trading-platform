# Operational Runbook Catalog

This folder holds the platform's operational runbooks — step-by-step procedures that the admin (and, in later phases, an operator on-call) follows when a specific incident or planned operation occurs. Runbooks exist because improvised responses under pressure are unsafe.

## Catalog

Status per runbook reflects whether it must exist at the current phase (per `REQ-LEGAL-005` gating).

| # | Runbook | File | Phase required | Status |
|---:|---|---|:---:|:---|
| 1 | DataSync failure recovery | `datasync-failure-recovery.md` | A | Stub in place, pending field-test refinement |
| 2 | Admin FYERS token re-auth | `admin-fyers-token-reauth.md` | A | Stub in place, pending field-test refinement |
| 3 | Data breach response (DPDP 72-hour notification) | `data-breach-response.md` | A | Stub in place, pending field-test refinement |
| 4 | EOD Signal Runner failure or abort | *not yet drafted* | B | Missing |
| 5 | HistoricDataSeed stuck or resume-from-failure | *not yet drafted* | B | Missing |
| 6 | Telegram bot token replacement | *not yet drafted* | B | Missing |
| 7 | Key Vault access failure | *not yet drafted* | B | Missing |
| 8 | MongoDB restore from point-in-time | *not yet drafted* | B | Missing |
| 9 | SQL Server restore (market data) | *not yet drafted* | B | Missing |
| 10 | SQL Server restore (backtest) | *not yet drafted* | B | Missing |
| 11 | sys_config rollback after a bad change | *not yet drafted* | B | Missing |
| 12 | Market halt manual override | *not yet drafted* | B | Missing |
| 13 | Global kill switch activation and deactivation | *not yet drafted* | B | Missing |
| 14 | Phase transition (A→B or B→C) | *not yet drafted* | B | Missing |
| 15 | User deactivation and reactivation | *not yet drafted* | B | Missing |
| 16 | FYERS app credential rotation | *not yet drafted* | B | Missing |

The three Phase A runbooks are mandatory before onboarding the first tester, per REQ-LEGAL-005 gating logic as surfaced through REQ-LEGAL-010's Legal Posture widget. The remaining entries are mandatory before the Phase B transition.

## Convention

Each runbook must include, at minimum, the following sections:

- **When to use.** The detection signal or trigger condition that tells the operator to open this runbook.
- **Preconditions.** What must be true before running the steps. What state to verify.
- **Steps.** Numbered, unambiguous, action-oriented. No branching logic embedded in prose; use sub-sections for decision points.
- **Verification.** How to confirm the incident is resolved. What telemetry, what UI state, what log line.
- **Rollback.** If any step goes wrong, how to back out safely.
- **Post-incident.** What to record, who to notify, when to update the runbook.
- **Last reviewed.** Date and reviewer. Per REQ-LEGAL-005, Phase B gate requires review within the past 90 days.

## Review cadence

- Every runbook must be reviewed at least once per 90 days during Phase B and Phase C.
- Every runbook should be reviewed after every incident that triggered its use, to capture lessons learned.
- Every review must be recorded in `audit_events` with the runbook identifier, reviewer, and review date.
- The admin Legal Posture widget (REQ-LEGAL-010) shows catalog completeness and review freshness at a glance.

## Authoring notes

Runbooks are written by the admin for the admin during Phase A. Once an operator team exists (Phase B or later), the runbooks must be written as if the reader is not the author — no implicit knowledge, no tribal shortcuts. Field-test each runbook after writing by handing it to someone unfamiliar and asking them to execute it without help.
