# Risk and Compliance Skill

Use this guide when implementing permissions, audit trails, live-signal safeguards, or portfolio-risk behavior.
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md) and [docs/portfolio-risk-guidelines.md](../../docs/portfolio-risk-guidelines.md).

## Read These Sections First

- personas and roles
- portfolio analytics and accounting
- trailing stops and exit alerts
- position sizing suggestions and risk
- admin operations

## Risk Focus

- prefer explainable and conservative defaults
- keep safety rules server-side
- log rule, threshold, subject, and outcome for important decisions
- keep overrides explicit and attributable

## Checklist

- privileged actions are reconstructable from audit data
- suppression and rejection reasons are visible
- safety logic behaves the same in research and live paths where intended
