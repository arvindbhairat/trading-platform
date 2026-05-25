# Documentation Map

## Purpose

This repository uses a single-source-of-truth documentation model.

Business and product requirements must live in one canonical reference file so they do not drift across roadmap notes, architecture explanations, and Claude skill files.

## Document Roles

- [terminology.md](./terminology.md)
  - canonical reference for all named system components, abbreviations, and domain term definitions; read this before writing requirements, architecture notes, or code
- [requirements-spec.md](./requirements-spec.md)
  - canonical source of truth for product requirements, constraints, and workflow rules
- [product-scope.md](./product-scope.md)
  - short product overview, scope summary, and major user journeys
- [system-architecture.md](./system-architecture.md)
  - architecture explanation: components, data ownership, and major flows
- [implementation-roadmap.md](./implementation-roadmap.md)
  - phased delivery plan derived from the canonical requirements
- [engineering-standards.md](./engineering-standards.md)
  - coding, testing, data, security, and operational standards
- [system-config.md](./system-config.md)
  - runtime configuration tiers, `sys_config` schema, categories, admin-management rules, and the required seed table (every mandated key with its default and source requirement)
- [portfolio-risk-guidelines.md](./portfolio-risk-guidelines.md)
  - deeper explanation of portfolio-risk and sizing defaults
- [data-management.md](./data-management.md)
  - MongoDB collection catalog: purpose, retention mechanism (TTL or Online Archive), active windows, and database setup checklist
- [repo-structure.md](./repo-structure.md)
  - preferred monorepo layout
- [project-structure.md](./project-structure.md)
  - practical map of the workspace folders and where to add code
- [adr/0001-documentation-structure.md](./adr/0001-documentation-structure.md)
  - decision log entry for this documentation structure
- [adr/0002-observability-and-configuration.md](./adr/0002-observability-and-configuration.md)
  - decision log entry for OTLP, Serilog, and centralized configuration strategy
- [adr/0003-rme-per-position-event-serialisation.md](./adr/0003-rme-per-position-event-serialisation.md)
  - per-position Channel-based event serialisation for RME correctness under concurrent LMDS/LADS events
- [adr/0004-corporate-action-detection-threshold.md](./adr/0004-corporate-action-detection-threshold.md)
  - corporate-action detection calibration threshold governing SuspendedForCorporateAction state
- [adr/0004-calibration-note-20260501.md](./adr/0004-calibration-note-20260501.md)
  - calibration note for corporate-action detection parameters
- [adr/0004-extract-shared-libraries.md](./adr/0004-extract-shared-libraries.md)
  - extraction of shared domain libraries from the API project to resolve Worker runtime dependency
- [adr/0005-mdp-server-side-websocket-migration-path.md](./adr/0005-mdp-server-side-websocket-migration-path.md)
  - migration path for server-side MDP WebSocket consumption and the conditions that trigger it
- [adr/0006-cnc-sandbox-verification.md](./adr/0006-cnc-sandbox-verification.md)
  - CNC sandbox verification procedure and acceptance criteria
- [adr/0007-v1-release-readiness.md](./adr/0007-v1-release-readiness.md)
  - v1 release readiness criteria and sign-off checklist
- [legal/](./legal/README.md)
  - versioned user-facing legal content: tester acknowledgement, short and long disclaimers, Terms of Service, and Privacy Policy. Referenced by the `REQ-LEGAL-*` and `REQ-PRIVACY-*` requirements
- [privacy/ropa.md](./privacy/ropa.md)
  - internal Record of Processing Activities required by `REQ-PRIVACY-010`. Operator-only document; the source of truth that the user-facing Privacy Policy summarises
- [operations/fyers-api-budget.md](./operations/fyers-api-budget.md)
  - quantified daily FYERS API call budget model required by `REQ-RATE-011` and used by `REQ-RATE-012` to warn on config changes that would breach the 80% threshold
- [operations/runbooks/](./operations/runbooks/README.md)
  - operational runbook catalog required by `REQ-LEGAL-005`; three runbooks are mandatory in Phase A (DataSync recovery, admin FYERS token re-auth, data-breach response), the remainder are required before Phase B
- [fyers_api_integration_guide.md](./fyers_api_integration_guide.md)
  - reference copy of FYERS API v3 documentation: REST endpoints, WebSocket, rate limits, and SDK availability
- [build_docs_reviews/](./build_docs_reviews/)
  - accumulated review notes on build-documentation quality; also stores the full-documentation review prompt used during doc-quality passes

## Editing Rules

- add or change a product requirement in `requirements-spec.md` first
- update `system-architecture.md` only when the implementation shape or system boundary changes
- update `implementation-roadmap.md` only to change sequencing or phase grouping
- update `engineering-standards.md` only for reusable engineering rules, not product behavior
- keep Claude skill files short and task-focused; they should point to the canonical docs rather than duplicate them
- record architecturally significant decisions in `docs/adr`
- when adding a requirement that introduces a new `sys_config` key, add the key to the required seed table in `system-config.md` at the same time

## Best-Practice Basis

This structure follows a few documentation best practices:

- separate authoritative reference from explanation and how-to material
- keep architecture explanations distinct from product requirements
- keep a decision log for significant trade-offs

Useful references:

- [Diataxis](https://diataxis.fr/)
- [arc42](https://arc42.org/overview)
- [C4 model](https://c4model.com/)
- [Architecture Decision Records](https://adr.github.io/)
