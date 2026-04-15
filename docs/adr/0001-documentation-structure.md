# ADR 0001: Canonical Requirements Spec and Derived Docs

## Status

Accepted

## Context

The initial documentation set captured valid requirements, but the same rule was often repeated in:

- product scope
- architecture
- roadmap
- engineering standards
- Claude skill files

That made the project easy to start, but it increased the risk of requirement drift and made it harder to know which file was authoritative.

## Decision

Adopt a documentation structure with one canonical requirements reference and several derived documents:

- `docs/requirements-spec.md` is the single source of truth for product requirements
- `docs/product-scope.md` summarizes mission, scope, and key user journeys
- `docs/system-architecture.md` explains how the platform is structured to satisfy the requirements
- `docs/implementation-roadmap.md` sequences delivery work without inventing new requirements
- `docs/engineering-standards.md` holds reusable engineering conventions only
- `.claude/skills/*` remain short implementation aids and must point back to the canonical docs

## Consequences

Positive:

- lower risk of requirement drift
- easier traceability for humans and Claude
- simpler future refinement because requirement changes start in one place

Trade-offs:

- the canonical requirements document becomes longer
- derived docs must be deliberately kept concise
- authors need discipline to avoid slipping new requirements into roadmap or skill files
