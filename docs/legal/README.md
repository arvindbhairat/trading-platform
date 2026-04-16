# Legal Content Artifacts

This folder holds versioned content artifacts that the platform serves to users or references in its operating posture. Each document is a plain-language artifact that Phase A ships with, intended to be reviewed and revised by a registered Indian fintech lawyer before Phase B transition per `REQ-LEGAL-005`.

These artifacts are distinct from the requirements themselves:

- `docs/requirements-spec.md` defines what the platform must do (the `REQ-LEGAL-*` and `REQ-PRIVACY-*` requirements).
- This folder holds the actual text the platform will show to users.

## Current Documents

| File | Purpose | Version |
|---|---|---|
| `tester-acknowledgement-v1.md` | The text every Phase A user accepts before gaining access. Referenced by `REQ-LEGAL-004`. | v1 |
| `disclaimer-short-v1.md` | Short-form disclaimer strings for Telegram, dashboard, chart advisory, and order modal. Referenced by `REQ-LEGAL-007`. | v1 |
| `disclaimer-long-v1.md` | Long-form disclaimer linked from every short-form surface and from the portal footer. Referenced by `REQ-LEGAL-007`. | v1 |
| `terms-of-service-v1.md` | The Terms of Service users accept at signup. Referenced by `REQ-LEGAL-008`. | v1 |
| `privacy-policy-v1.md` | The Privacy Policy users accept at signup. Referenced by `REQ-LEGAL-008` and `REQ-PRIVACY-002`. | v1 |

## Not Yet Drafted

- **Personal data breach runbook** — required by `REQ-PRIVACY-006`. Lives at `docs/operations/runbooks/data-breach-response.md`; the v1 stub is in place.

The internal Record of Processing Activities required by `REQ-PRIVACY-010` lives at `docs/privacy/ropa.md` (separate folder because it is operator-internal, not user-facing).

## Versioning and Change Flow

1. Draft changes in a new file (for example `tester-acknowledgement-v2.md`) rather than editing the existing version in place. Previous versions must remain in the repo because prior acceptance records reference them.
2. Update the corresponding `sys_config` version key (`legal.tester_acknowledgement.current_version`, `legal.tos.current_version`, `legal.privacy.current_version`, `legal.disclaimer.long_version`).
3. Platform behaviour on version bump is governed by `REQ-LEGAL-004` and `REQ-LEGAL-008` — affected users must re-accept on next login before protected features unlock.
4. Every version change is recorded in `audit_events`.

## Review Cadence

Until Phase B, the platform operator is the sole reviewer and author of these documents. At the Phase B gating step, a registered Indian fintech lawyer must review every document in this folder and the Privacy Notice, and record the review in `audit_events` per `REQ-LEGAL-005`. After Phase B transition, any material change to these documents should go through legal review before the version is bumped.
