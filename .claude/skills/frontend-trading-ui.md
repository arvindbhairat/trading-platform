# Frontend Trading Portal Skill

Use this guide when building the unified web portal (`apps/web`).
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md).

## Design System (mandatory — read first)

Before any UI work, consult the design system at `design_system/`. All visual decisions must derive from it. See `docs/engineering-standards.md` § "Design System Standards" for the full rules.

Key files:
- `design_system/colors_and_type.css` — all design tokens (colors, spacing, type scale, shadows, motion)
- `design_system/mock_screens/` — full-page layout references
- `design_system/preview/` — visual examples of every component and token category
- `apps/web/src/components/primitives.tsx` — React/TypeScript implementations of all design system primitives (Icon, Logo, Pill, Btn, Card, Num, Label, etc.)
- `apps/web/src/app/globals.css` — web-app copy of the token CSS (imported by layout.tsx), plus type scale classes

### Hard rules

- No hardcoded colors, spacing, font sizes, or shadows. Use CSS variables from `globals.css`.
- Reuse components from `src/components/primitives.tsx` before creating new ones.
- Match `design_system/mock_screens/` layout and spacing exactly.
- New components must be added to `primitives.tsx`, not scattered across pages.
- Invalid implementation = any commit that introduces ad-hoc inline styles or bypasses the token system.

## Read These Spec Sections First

- charting and decision support
- notifications and signal delivery
- portfolio analytics and accounting
- manual sync recovery and manual adjustment
- admin operations

## Frontend Focus

- keep server truth authoritative
- make realtime freshness and stale states visible
- keep chart interactions smooth across timeframe changes
- preserve symbol state, portfolio state, and signal context on deep links
- keep user and admin navigation visually unified but permission-aware
- surface risk, alert, mismatch, and adjustment state clearly

## UI Checklist

- clear loading, empty, stale, and error states
- accessible controls and keyboard flow
- mobile-safe layouts for core pages
- explicit confirmation for irreversible actions
- visible context for holdings, orders, trades, stops, and alerts
- all styles use design tokens (no hardcoded values)
- all components reuse or extend primitives
