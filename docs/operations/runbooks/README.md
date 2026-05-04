# Operational Runbook Catalog

This folder holds the platform's operational runbooks — step-by-step procedures that the admin (and, in later phases, an operator on-call) follows when a specific incident or planned operation occurs. Runbooks exist because improvised responses under pressure are unsafe.

## Catalog

Status per runbook reflects whether it must exist at the current phase (per `REQ-LEGAL-005` gating).

| # | Runbook | File | Phase required | Status |
|---:|---|---|:---:|:---|
| 1 | DataSync failure recovery | `datasync-failure-recovery.md` | A | Refined in P8-T7 — reviewed 2026-05-05 |
| 2 | Admin FYERS token re-auth | `admin-fyers-token-reauth.md` | A | Refined in P8-T7 — reviewed 2026-05-05 |
| 3 | Data breach response (DPDP 72-hour notification) | `data-breach-response.md` | A | Refined in P8-T7 — reviewed 2026-05-05 (initial authoring P2-T22) |
| 4 | RME concurrency-freeze admin resolution | `rme-concurrency-freeze-resolution.md` | A | Finalized in P8-T7 — reviewed 2026-05-05 (authored in P6-T8) |
| 5 | MongoDB replica-set failover during LADS polling | `mongodb-failover-lads-recovery.md` | A | Finalized in P8-T7 — reviewed 2026-05-05 (authored in P6-T8) |
| 6 | EOD Signal Runner failure or abort | *not yet drafted* | B | Missing |
| 7 | HistoricDataSeed stuck or resume-from-failure | *not yet drafted* | B | Missing |
| 8 | Telegram bot token replacement | *not yet drafted* | B | Missing |
| 9 | Key Vault access failure and secret recovery | `keyvault-secret-recovery.md` | B | Authored in P1-T10 (REQ-BCP-004/006/008) |
| 10 | MongoDB restore from point-in-time | `mongodb-pitr-restore.md` | B | Authored in P1-T10 (REQ-BCP-001/006) |
| 11 | SQL Server restore (market data) | `sqlserver-restore-market-data.md` | B | Authored in P1-T10 (REQ-BCP-002/006) |
| 12 | SQL Server restore (backtest) | `sqlserver-restore-backtest.md` | B | Authored in P1-T10 (REQ-BCP-003/006) |
| 13 | sys_config rollback after a bad change | *not yet drafted* | B | Missing |
| 14 | Market halt manual override | *not yet drafted* | B | Missing |
| 15 | Global kill switch activation and deactivation | *not yet drafted* | B | Missing |
| 16 | Phase transition (A→B or B→C) | *not yet drafted* | B | Missing |
| 17 | User deactivation and reactivation | *not yet drafted* | B | Missing |
| 18 | FYERS app credential rotation | *not yet drafted* | B | Missing |

**Phase A mandatory runbooks (5 total):** Runbooks 1–5 are mandatory before onboarding the first tester, per REQ-LEGAL-005 gating logic as surfaced through REQ-LEGAL-010's Legal Posture widget. Runbooks 4 and 5 were added as Phase A mandatory in the 2026-04-25 review (findings RME-L2 and A-12): the OCC-exhaustion concurrency-freeze path (REQ-RME-CONC-007) and the MongoDB replica-set failover / sustained LADS abort window (REQ-PORT-021a) are both realistic Phase A production scenarios that must have deterministic resolution procedures before the RME is in production. The remaining entries are mandatory before the Phase B transition.

**Runbook #4 (RME concurrency-freeze resolution) must cover:** (a) detecting the frozen-with-visibility state in the admin System Health panel and OTLP traces; (b) verifying position document consistency in MongoDB before clearing the RME incident flag; (c) the specific admin action sequence to release the freeze without data loss; (d) post-incident verification steps confirming the affected position channel has resumed normal event processing.

**Runbook #5 (MongoDB failover / LADS recovery) must cover:** (a) detecting that MongoDB replica-set failover has produced a sustained LADS abort window (three consecutive aborts per REQ-PORT-021a); (b) expected Worker behaviour during failover (retryWrites in flight, channel consumer retry exhaustion → admin advisory); (c) verifying replica-set primary election is complete before clearing the admin advisory; (d) confirming LADS resumes normal polling and the sustained-failure flag clears.

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
