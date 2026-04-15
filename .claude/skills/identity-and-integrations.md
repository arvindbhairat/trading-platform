# Identity and Integrations Skill

Use this guide when implementing OAuth, FYERS integration, or Telegram delivery.
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md).

## Read These Sections First

- identity, access, and external authentication
- notifications and signal delivery
- admin operations

## Integration Focus

- keep provider behavior behind adapters
- separate admin-scoped and user-scoped FYERS auth paths
- persist token lifecycle state explicitly
- treat dirty-token handling as a first-class operational state
- make delivery failures observable and diagnosable

## Checklist

- provider credentials are encrypted and never logged in plaintext
- invalid or expired auth leads to clear recovery paths
- Telegram delivery attempts and outcomes are durable
- OAuth identity linking stays consistent across providers
