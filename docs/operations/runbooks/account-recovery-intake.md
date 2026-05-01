# Account Recovery Intake Procedure

REQ-RECOVERY-004 / REQ-RECOVERY-005 — Grievance Officer intake procedure for processing user-submitted account recovery requests.

## When to use

- A user contacts the Grievance Officer via email stating they have lost access to their account and requesting recovery.
- The email includes (or attempts to include) the three required verification items listed on the public `/recovery-request` page.
- The request cannot be resolved through standard OAuth account recovery (e.g., "Forgot password" at the provider's end), or the user confirms their original OAuth provider is completely unavailable.

## Preconditions

- The Grievance Officer email inbox is monitored at least once per business day.
- The admin has an active session with valid step-up re-authentication on the Signal Stack admin portal.
- The admin has access to the user lookup functionality (by email, userId, or FYERS account number) through the admin portal.
- The `POST /api/v1/auth/admin/recover` endpoint is operational (P2-T6).

## Steps

### 1. Receive and acknowledge the request

1. Read the incoming email. Verify the subject line follows the convention `Account Recovery Request — [Name]`. If not, note the discrepancy but do not reject solely on subject format.
2. Reply to the user within **7 calendar days** acknowledging receipt. Include the case reference number (use format `RECOV-YYYYMMDD-XXX` where XXX is a zero-padded sequence number for that day).
3. Record an `audit_events` row with event type `recovery_request_acknowledged` containing the case reference and the sender's email.

### 2. Verify the three required items

The user must provide **all three** of the following verification items (per REQ-RECOVERY-004 and the `/recovery-request` page):

- **Original OAuth email** — The email address the user originally used to sign in.
- **FYERS account number or client ID** — The user's FYERS trading account identifier.
- **Recent trade reference** — Any trade identifier (trade ID, order ID, or trade date + symbol + quantity).

For each item:

| Item | How to verify |
|---|---|
| Original OAuth email | Search the admin user database by email. If the user record exists, confirm the email matches. If the email is not found, request the user check for typos or alternate emails. |
| FYERS account number or client ID | Cross-reference against the user's `fyers_tokens` record (if available). If no token record exists, ask the user to provide additional documentation (e.g., a screenshot of their FYERS dashboard showing the account number). |
| Recent trade reference | Check the user's trade ledger (`trade_ledger`) for matching trade activity. If the ledger is empty or the trade cannot be found, ask for a more specific reference. |

**Outcomes:**

- **All three verified** → Proceed to step 3.
- **One or more items incorrect** → Reply to the user explaining which items could not be verified and what corrected information is needed. Reset the 7-day acknowledgement clock only if this is the first follow-up.
- **Two or more failed verification attempts** → Escalate to the platform administrator (if different from the Grievance Officer) for manual review. Record an `audit_events` row with event type `recovery_escalated`.

### 3. Initiate the admin-assisted rebind

Once all three verification items are confirmed:

1. Log in to the Signal Stack admin portal.
2. Perform step-up re-authentication (REQ-SEC-011).
3. Navigate to the account recovery section and enter the target user's ID (obtained from the user lookup in step 2).
4. Choose the new OAuth provider (Google or Microsoft — Facebook is not permitted for account recovery per REQ-ROLE-007).
5. Submit the recovery via the admin recovery endpoint (`POST /api/v1/auth/admin/recover`). The platform will:
   - Rebind the user's account to the new OAuth identity.
   - Clear existing linked identities.
   - Write before/after audit events.
6. Verify the response confirms `recovered = true`.

### 4. Notify the user

1. Send an email to the user confirming the recovery is complete.
2. Instruct the user to sign in using the new OAuth identity on their next login attempt.
3. Inform the user that they will need to re-authenticate with FYERS on their next sign-in.
4. Record an `audit_events` row with event type `recovery_completed` containing the case reference and summary.

## Verification

- The admin recovery API returns `{ recovered: true, new_user_id: "..." }`.
- An audit event of type `admin_recovery_rebind` is written with before/after state.
- The user can sign in with the new OAuth identity and is prompted to re-authenticate with FYERS.
- The user's previous OAuth identity can no longer sign in to the account.

## Rollback

If the rebind completes but the user still cannot access the account:

1. Verify the user is using the correct new OAuth identity (not the old one).
2. If the user has lost access to the new identity as well, repeat the intake procedure with the corrected identity.
3. If the rebind produced incorrect state (wrong user, wrong provider), contact the platform administrator to manually restore from the most recent MongoDB backup. This is a data-integrity incident and must be recorded as an incident in `audit_events`.

## Post-incident

1. Record the case reference, verification outcomes, and resolution in `audit_events`.
2. If the recovery revealed a gap in the intake procedure, update this runbook.
3. If two or more failed verification attempts occurred, consider whether the public `/recovery-request` page requires clearer instructions.

## Last reviewed

- **Date:** 2026-05-01
- **Reviewer:** AI-assisted authoring (P2-T23)
