# Runbook 3 — Personal-Data Breach Response

**REQ coverage:** REQ-PRIVACY-006  
**Phase required:** A
**Regulatory basis:** DPDP Act 2023 Section 8 (breach notification)
**Notification deadline:** 72 hours from detection
**Last reviewed:** 2026-05-01 (initial authoring — field-test before Phase A user onboarding)
**Owner:** Platform admin (also the designated grievance officer per REQ-PRIVACY-005)

---

## Why this exists

DPDP requires personal-data breach notification to the Data Protection Board of India and to affected users within 72 hours of detection. This deadline is short. Improvising the notification format, recipient list, and internal containment under pressure is how compliance obligations get missed. This runbook exists so the response is mechanical rather than invented in the moment.

**A personal-data breach is any unauthorised access, disclosure, loss, or alteration of personal data.** The definition is broad. When in doubt, treat it as a breach and follow this runbook; you can stand down the notification later if the investigation concludes it was not a breach, but you cannot retroactively meet the 72-hour deadline.

## When to use

Open this runbook immediately on any of these triggers:

- Evidence of unauthorised access to the platform databases, Key Vault, or admin accounts.
- Accidental disclosure of personal data (for example, a mis-sent email including another user's data, a public-repo commit of secrets or user data, a mis-configured storage bucket).
- Loss of a backup tape, credential, or device containing personal data.
- A third-party processor (Azure, FYERS, Telegram, observability vendor) notifies the platform of a breach on their side affecting the platform's data.
- A user reports what appears to be exposure of their data.
- A suspicious security scan result that cannot be quickly ruled out.

## The 72-hour clock starts the moment detection occurs

Record the detection timestamp immediately. The clock does not start on confirmation or on remediation; it starts on *detection* — the moment a reasonable observer would say "this might be a breach."

## Steps

### Step 1 — Record detection and initiate the incident (within 30 minutes)

1. Record the event in `audit_events` with category `breach_suspected`. Capture: detection timestamp, discovery source, initial indicators, systems believed affected, approximate user count if estimable.
2. Send yourself an admin-only operational notification to establish the timeline in Telegram and email (REQ-NOTIFY-021 routes breach events to both channels unconditionally).
3. Note the 72-hour deadline in your calendar as a hard stop. Work backward from it.

### Step 2 — Contain (within 2 hours of detection)

Halt the active compromise before investigating deeply. Specific actions depend on the trigger:

- **Suspected credential compromise:** rotate the affected secret immediately per REQ-BCP-008 — FYERS app secret, Telegram bot token, Key Vault keys, session signing keys as applicable. Revoke admin sessions and force re-authentication.
- **Suspected admin account compromise:** sign out all admin sessions, force admin MFA re-enrolment at the IdP, investigate OAuth sign-in logs for the admin identity.
- **Suspected data store access:** revoke affected Managed Identity or connection, restore from the last known-clean backup if necessary (REQ-BCP-001 RPO 1 hour bounds the loss), and isolate the affected database behind firewall rules until cleared.
- **Third-party processor breach:** review their notification, assess what platform data they held, and verify your Azure region / India-scope constraints (REQ-LEGAL-009) were not violated.

### Step 3 — Investigate (within 24 hours)

Determine:

- Exact scope: which personal data categories, how many data principals, what time window.
- Cause: attack vector, misconfiguration, insider action, or third-party failure.
- Current status: contained, still active, unknown.
- Evidence chain: log extracts, access records, user reports, preserved for later submission.

Record findings in the incident record in `audit_events` on an ongoing basis. Treat each update as append-only.

### Step 4 — Notify the Data Protection Board of India (before the 72-hour deadline)

Use the Board's prescribed notification format (verify the current format on the DPB-I website; it may update over time). At minimum, the notification must state:

- Nature of the breach.
- Categories and approximate number of data principals affected.
- Categories and approximate volume of personal data affected.
- Likely consequences of the breach.
- Measures taken or proposed to address the breach.
- Contact information for the grievance officer (REQ-PRIVACY-005).

Submit through the official channel (email to the DPB-I designated address, or online portal if one is available). **Retain proof of submission** — email sent confirmation, portal receipt, timestamp — attached to the incident record.

### Step 5 — Notify affected users (before the 72-hour deadline)

Notify every affected data principal directly. Content must include, in plain language:

- What happened, without technical jargon.
- What personal data of theirs was affected.
- What the platform has done in response.
- What the user should do (for example, rotate external credentials, watch for suspicious activity).
- How to contact the grievance officer for questions.

Delivery channel: primary email to the user's registered OAuth email; secondary in-portal notification on next sign-in. Do not use Telegram for breach notifications — Telegram is not an appropriate formal notification channel. Use the admin-ops email channel (REQ-NOTIFY-020) for the template if needed.

Record the user-notification dispatch in `audit_events`.

### Step 6 — Remediate

Address the root cause. Depending on the breach nature, this may include:

- Patching vulnerable dependencies (REQ-SEC-007).
- Revising access controls.
- Adding detection rules.
- Rotating all credentials, not just the compromised one.
- Re-training the admin on the specific failure mode if it was an operator error.

Record all remediation steps in the incident record.

### Step 7 — Post-incident review (within 14 days)

Conduct a review covering:

- Timeline from detection to notification to remediation.
- Root cause and contributing factors.
- What detection signals worked; which were missed.
- What this runbook missed; revise it.
- Any follow-up actions with owners and deadlines.

Record the review in `audit_events`.

## Rollback

No rollback is possible for a notification that has been issued. Do not issue the DPB-I notification until Step 2 (containment) is underway — but do not delay it past the deadline for investigation perfection either. Better to notify with partial scope estimates and update later than to miss the deadline.

## Post-incident

- File the complete incident record, including all `audit_events` entries, the DPB-I submission proof, the user-notification dispatch record, and the post-incident review, in a single incident folder kept in operational records retention.
- Run the annual breach procedure drill before the next anniversary — this runbook itself is exercise-tested per REQ-PRIVACY-006.
- Update the **Last reviewed** date at the top of this runbook.

## Notes

- **Do not post about the breach publicly** on social media or marketing channels. Notification channels are the DPB-I, affected users, and (if applicable) the operating entity's board. Public communication beyond that is a strategic decision, not a compliance obligation, and should not be made in the heat of the incident.
- **Do not destroy logs** during the investigation. Evidence preservation is critical.
- **Preserve chain of custody** on any forensic work. If a third-party investigator is engaged, record the handoff in `audit_events`.
- **In Phase A with 30 testers**, a breach affecting all users is still a breach. Small scale does not reduce the obligation. It may simplify the notification content, though, which is a practical benefit.
- **Grievance officer name on the notification:** during Phase A this is the admin's own name and email per REQ-PRIVACY-005.
