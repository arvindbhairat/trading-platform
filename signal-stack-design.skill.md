---
name: signal-stack-design
description: Enforce Signal Stack design system in apps/web — tokens, mock screens, primitives, UI kits, preview references. Follow agent.md step 5 design gate before writing UI code.
---

# Signal Stack Design System — Agent Skill

Use this skill when building or modifying UI in `apps/web`. It is a quick-reference companion to the mandatory design gate procedure in `execution_plan/agent.md` step 5.

## Design gate (required before any UI work)

Before writing any frontend code, you MUST follow the procedure in `execution_plan/agent.md` step 5:

1. Read the relevant mock screen from `design_system/mock_screens/`
2. Read the relevant UI kit from `design_system/ui_kits/` (user-portal or admin-portal)
3. Map components from `apps/web/src/components/primitives.tsx` (production TSX mirror)
4. Use only design tokens from `apps/web/src/app/globals.css`
5. Validate against `design_system/preview/` reference HTML files
6. Document the consulted mock screen in the task log under **Design system compliance**

## Token reference (quick lookup)

| Token category | Prefix | Example |
|---|---|---|
| Backgrounds | `--bg-*` | `--bg-0` (canvas), `--bg-1` (surface), `--bg-2` (raised) |
| Foreground | `--fg-*` | `--fg-1` (primary), `--fg-2` (body), `--fg-3` (label) |
| Brand | `--brand-*` | `--brand-500` (amber primary) |
| Up (green) | `--up-*` | `--up-500`, `--up-bg` |
| Down (red) | `--down-*` | `--down-500`, `--down-bg` |
| Status | `--warn-*`, `--info-*`, `--neutral-*` | |
| Lines | `--line-*` | `--line-1` (hairline), `--line-3` (focus) |
| Spacing | `var(--s-*)` | `var(--s-4)` not `16px` |
| Radii | `var(--r-*)` | `var(--r-sm)`, `var(--r-md)` |
| Typography | `t-*` classes | `t-h1`–`t-label-sm`, `t-body-*`, `t-num-*` |

## Hard prohibitions

- No hardcoded colors (`color: "#..."`, `background: "#..."`)
- No hardcoded spacing (`padding: "..."`, `margin: "..."`)
- No arbitrary font sizes or weights (`font-size:`, `font-weight:`)
- No inline styles with ad-hoc values
- No duplicating components that exist in `src/components/primitives.tsx`
- No external UI libraries that conflict with the design system
- No CSS outside the token system defined in `globals.css`

## Source of truth files

- `design_system/colors_and_type.css` — canonical CSS variables
- `design_system/mock_screens/tokens.css` — identical token set for mock screens
- `design_system/ui_kits/user-portal/Primitives.jsx` — design system primitives source
- `design_system/ui_kits/user-portal/AppShell.jsx` — 3-zone layout shell
- `design_system/ui_kits/admin-portal/` — admin-specific components
- `design_system/preview/` — visual reference HTML files
- `design_system/mock_screens/` — full-page mockups

See `docs/engineering-standards.md` § "Design System Standards" for the complete rules.
