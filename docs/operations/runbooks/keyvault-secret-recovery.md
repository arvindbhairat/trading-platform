# Runbook 9 — Key Vault Access Failure and Secret Recovery

**REQ coverage:** REQ-BCP-004, REQ-BCP-006, REQ-BCP-008  
**Last reviewed:** 2026-05-05 — drill-execution-001 (P8-T8); field-test required before Phase A user onboarding

---

## When to use

Open this runbook when any of the following occur:

**Access failure scenarios:**
- API or Worker fails to start with a Key Vault authentication error (`KeyVaultAccessDenied` or `KeyVaultSecretNotFound`).
- Application logs show repeated `SecretClientException` from the Azure Key Vault SDK.
- Azure Monitor fires a Key Vault availability alert.

**Secret recovery scenarios:**
- A secret is accidentally deleted from Key Vault and must be recovered from the soft-delete store.
- A secret is corrupted or written with an incorrect value and must be rolled back to a prior version.
- A secret rotation has failed mid-flight and the current version is in an inconsistent state.
- A restore drill exercises the Key Vault recovery path per REQ-BCP-007.

Do **not** use this runbook for planned secret rotation — follow the standard rotation procedures defined in REQ-BCP-008 instead.

---

## Preconditions

- You have Key Vault Administrator or Owner access on the Key Vault resource.
- You have Azure CLI or Azure portal access.
- The affected service (API or Worker) has been identified.
- If a secret was compromised: the new replacement secret value is ready (from the relevant provider, e.g., FYERS, Telegram, etc.).

---

## Steps

### Scenario A — Key Vault access failure (authentication or RBAC)

#### A-1. Confirm the managed identity assignment

1. Azure portal → App Service (API or Worker) → Identity → System-assigned.
2. Confirm "Status: On" and note the Object (principal) ID.
3. Navigate to the Key Vault → Access control (IAM).
4. Confirm the managed identity has the `Key Vault Secrets User` role assignment.
5. If missing: click "Add role assignment" → `Key Vault Secrets User` → assign to the managed identity.
6. Wait 5 minutes for RBAC propagation, then restart the affected App Service.

#### A-2. Confirm network access

1. Key Vault → Networking → Firewalls and virtual networks.
2. Confirm the App Service's VNet integration subnet is listed in "Virtual network rules" or that "Allow trusted Microsoft services" is enabled.
3. If missing: add the subnet or enable the trusted services bypass.

#### A-3. Confirm Key Vault is not in a degraded state

1. Azure Service Health → Key Vault → check for active incidents in Central India region.
2. If an Azure-side incident is active: wait for resolution; Key Vault SDK has built-in LKG cache per REQ-CONFIG-001 that serves values for up to `config.last_known_good.max_age_seconds`.

---

### Scenario B — Recover a soft-deleted secret

1. Open Azure CLI or Azure portal → Key Vault → Objects → Secrets → "Show deleted secrets."
2. Locate the deleted secret by name.
3. Click "Recover" (or run `az keyvault secret recover --name <name> --vault-name <vault>`).
4. Confirm the recovered secret is re-enabled and its value is intact.
5. Restart the affected App Service.
6. Verify the application reads the recovered secret successfully (check OTLP logs for `KeyVaultSecretRetrieved` trace).

**Important:** Soft-delete retention is 90 days per REQ-BCP-004. After 90 days, a deleted secret is permanently purged. If the 90-day window has passed, the secret value must be re-provisioned from the source (see Scenario C).

---

### Scenario C — Roll back to a prior secret version

1. Key Vault → Secrets → select the affected secret.
2. Review the version history. Identify the last known-good version by its `Created` timestamp.
3. Select the prior version.
4. Copy the value from the prior version.
5. Click "New Version" and paste the prior value to make it the current (latest) version.
6. Disable the incorrect version by selecting it and clicking "Disable."
7. Restart the affected App Service.
8. Monitor OTLP traces to confirm the application is reading the correct version.

---

### Scenario D — Intent HMAC key recovery for audit verification

The `intent-hmac-key` secret is versioned and old versions must never be purged (REQ-BCP-008, S-13).

1. Identify the key version needed (from `intent_ledger.payload_signature` version identifier).
2. Key Vault → Secrets → `intent-hmac-key` → version list.
3. Locate the version by its creation timestamp.
4. Use the version's value to verify the `payload_signature` for audit purposes.
5. Do NOT delete or disable old versions. Do NOT use old versions to sign new payloads.

---

### Scenario E — Emergency secret rotation after compromise

If a secret is suspected or confirmed to be compromised:

1. Immediately revoke the compromised credential at the source provider (FYERS, Telegram, etc.).
2. Provision a new credential from the source provider.
3. In Key Vault → Secrets, select the affected secret name and click "New Version."
4. Enter the new credential value.
5. Confirm the new version is enabled and the old version is disabled.
6. Restart API and Worker to pick up the new secret.
7. Verify the application is using the new credential (confirm successful authentication to the provider in OTLP traces).
8. Write an `audit_events` record with `event_type: secret_rotation_emergency` and all relevant details.

---

## Verification

- Affected App Service starts without Key Vault errors.
- OTLP traces show `KeyVaultSecretRetrieved` with `status: success` for the affected secret.
- No `SecretClientException` or `KeyVaultAccessDenied` in the structured log output.
- For Scenario B/C: confirm the application is using the recovered/rolled-back value by testing the affected feature (e.g., FYERS token validation, Telegram test-send).

---

## Rollback

If the recovery or rotation makes things worse:

1. Stop the affected App Service.
2. Revert the Key Vault secret to the previous enabled version.
3. Restart the App Service.
4. Record the rollback decision in the incident log.

---

## Post-incident

1. Write an `audit_events` record:
   - `event_type`: `keyvault_secret_recovery` | `keyvault_access_failure_resolved` | `secret_rotation_emergency`
   - `secret_name`: (name of affected secret — do NOT include the value)
   - `outcome`: `success` or `failure`
   - `operator`: admin identity
   - `notes`: root cause and resolution summary
2. For emergency rotation events: confirm the compromised credential is deactivated at the source provider.
3. If this was a restore drill, record `event_type: bcp_drill_keyvault`.
4. Update this runbook if any steps were missing or incorrect.
5. If RBAC was the root cause: audit all Key Vault role assignments and remove any unexpected entries.

---

**Last reviewed:** 2026-05-05 — drill-execution-001 (P8-T8); field-test required before Phase A user onboarding
