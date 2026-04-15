# Admin FYERS Token Re-authentication

**Phase required:** A
**Last reviewed:** *(not yet reviewed — stub)*
**Owner:** Platform admin

## Why this exists

FYERS access tokens are day-scoped (REQ-AUTH-008). When FYERS is the configured market data provider, the admin's personal FYERS token must be valid for DataSync, LMDS, and any admin-token-dependent job to function. A missing or dirty admin token halts shared market data ingestion platform-wide. REQ-MARKET-004 calls this out as an operator responsibility with no automated proactive reminder — it relies on the persistent admin-portal warning (REQ-SESSION-009) and this runbook.

**Frequency of use: daily** whenever FYERS is the active market data provider. Routine, not exceptional.

## When to use

- Admin sign-in shows the persistent high-visibility warning that the admin FYERS token is dirty, expired, or missing (REQ-SESSION-009).
- The System Health widget shows the admin FYERS token in a warning or critical state (REQ-ADMIN-014).
- An admin-only operational notification reports DataSync or LMDS authentication failure caused by an expired admin token.
- Morning pre-trading routine — perform proactively before DataSync and LMDS need the token.

## Preconditions

- The admin is signed into the portal.
- The admin has access to their FYERS account credentials (login + 2FA device).
- No concurrent admin is performing the same operation. Only one admin exists per REQ-ROLE-001, so this is usually trivially true.

## Steps

### Step 1 — Open the admin FYERS token view

Navigate to **Admin → Integrations → FYERS → Admin token**. The page shows the current token status (valid, expired, dirty, or missing), the last successful issuance timestamp, and a **Re-authenticate** button.

### Step 2 — Initiate FYERS OAuth

Click **Re-authenticate**. The portal redirects to the FYERS OAuth authorization page using the platform's registered FYERS app credentials (during Phase A this is the admin's personal FYERS app; REQ-AUTH-003).

### Step 3 — Complete FYERS authorization

On the FYERS hosted page, sign in with the admin FYERS account and complete any 2FA prompts. Grant the authorization.

FYERS will redirect back to the platform's registered callback URL. The platform then exchanges the authorization code for an access token and stores it in `fyers_tokens` with `owner=admin`.

### Step 4 — Verify success

Back on **Admin → Integrations → FYERS → Admin token**, confirm:

- Status is now "valid."
- "Issued at" shows the current timestamp.
- The persistent high-visibility warning in the admin portal header has cleared.
- The System Health widget's admin FYERS token row is green.

### Step 5 — Resume dependent jobs

If DataSync, LMDS, or any admin-token-dependent job was in a suspended or failed state because of the dirty token, they should resume automatically on their next scheduled trigger. If they were waiting for manual intervention (for example, a DataSync run that failed and needs re-trigger), navigate to the respective job page and trigger it manually now.

## Rollback

None is needed. Token issuance is idempotent in the sense that a new successful issuance supersedes the prior one without side effects. If the new token is itself faulty, repeat the steps to re-issue.

## Post-incident

- If the token expired during a trading session and caused any job failure, note the gap in any affected downstream data (missed DataSync, LMDS coverage gap). Consider whether a targeted HistoricDataSeed reseed is needed for symbols whose candles were missed.
- If this runbook was opened because of repeated intra-day token expiration (unexpected — FYERS tokens should survive a full trading day), escalate to investigating the FYERS app configuration.
- Update the **Last reviewed** date at the top of this runbook.

## Notes

- The persistent admin-portal warning (REQ-SESSION-009) is the only notification mechanism for a missing or dirty admin token. No automated proactive reminder exists (REQ-MARKET-004); this is a deliberate operator responsibility. Set yourself a daily recurring reminder outside the platform — calendar alert, phone alarm — so you renew the token before market hours begin.
- When the active market data provider is switched to TrueData or GDF, this runbook becomes optional for DataSync and LMDS purposes. The admin FYERS token is still required for user account data routing through the admin context, but the daily renewal pressure reduces dramatically (REQ-MARKET-004).
- At Phase B, this runbook will be revised: the commercial FYERS app replaces the personal app, and the access pattern may change.
