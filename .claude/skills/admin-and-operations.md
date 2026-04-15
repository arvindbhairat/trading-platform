# Admin and Operations Skill

Use this guide when implementing user approval, universe management, calendar management, or operational control surfaces.
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md).

## Read These Sections First

- personas and roles
- universe management and symbol master
- trading calendar and timeframes
- market data and EOD sync
- admin operations

## Admin Focus

- keep every privileged action explicit and auditable
- separate archive state from scan-exclusion state
- make job status, retries, and failure reasons easy to inspect
- preserve historical availability when universe membership changes

## Checklist

- approval actions are permission-checked and logged
- CSV outcomes are clear and reviewable
- calendar edits are auditable
- manual job reruns are controlled and observable
