# Signal Stack Design System

**Signal Stack** is a browser-based decision-support platform for NSE Nifty 500 equities. It is **long-only**, **research- and notification-first**, and **never places orders autonomously** — every output is an advisory tied to the user's own configured scans and risk profile.

This design system encodes the brand's visual foundations, voice & tone, and UI components for two surfaces: the unified **User Portal** (trader dashboard, charts, Signal Builder, notifications, order confirmation) and the **Admin Portal** (user approval queue, universe management, trading calendar, job controls, legal posture widget).

---

## Source Material

- **Spec repo:** `github.com/arvindbhairat/trading-platform` (private) — read on demand
- **Key canonical docs consulted:**
  - `docs/product-scope.md` — product goal and user journeys
  - `docs/terminology.md` — canonical component names (Signal, RME, DataSync, EODSR, LMDS, LADS, NDJ…)
  - `docs/requirements-spec.md` — all `REQ-*` ids (UI language in `REQ-LEGAL-006`, dashboard in `REQ-PORT-*`, admin in `REQ-ADMIN-*`)
  - `CLAUDE.md` — technology stack (Next.js + TypeScript, ASP.NET Core, MongoDB, PostgreSQL, Azure)

This design system is **not** pre-loaded with any visual assets from the repo (there are none — the repo is spec-only, no code yet). The visual language below was derived from the product's **regulatory posture**, **domain**, and **target persona** (self-directed Indian retail trader using FYERS + TradingView-lightweight-charts).

---

## Content Fundamentals (voice, tone, copy rules)

Signal Stack's language is constrained by `REQ-LEGAL-006`: the platform is decision-support, not advice. Every word a user sees must reinforce that they are the decision-maker.

### Voice attributes

- **Self-directed, not prescriptive.** Signal Stack shows the user *what they configured*, not what Signal Stack thinks.
- **Precise and quiet.** Financial copy is specific. No exclamation marks, no hype, no emoji as decoration.
- **Confident about the math, humble about outcomes.** We can be sure we calculated the stop correctly; we are never sure it is the right trade.
- **Technical when it helps, plain otherwise.** "R-multiple" is fine on the dashboard — it's the vocabulary of this user. "Stop level" is preferred over "SL" in body copy; "SL" is fine on dense tables.

### Banned verbs & phrasings

Never: *buy, sell, recommended, suggested pick, top pick, advised, signal to buy, signal to sell, our analysts, best trade, strong buy, actionable idea*.

### Preferred phrasings

| Don't say | Do say |
|---|---|
| "Buy RELIANCE at ₹2,840" | "Your Volume Spike scan matched **RELIANCE** at ₹2,840 — your configured entry" |
| "Recommended stop: ₹2,760" | "Stop per your risk profile: ₹2,760" |
| "Signal to exit" | "Stop level breached — your configured exit condition" |
| "Top 5 stocks today" | "5 symbols matched your active subscriptions today" |
| "Buy now" | "Review on chart" / "Confirm order" (only in the FYERS handoff modal) |

### Tone: by surface

- **Dashboard:** factual, terse, number-forward. `"+2.4% this week"`, `"3 open positions · 2 at trailing stop"`.
- **Notifications (Telegram & in-portal):** one sentence of context, one line of numbers, one disclaimer footer. Never call-to-action-y.
- **Empty states:** encouraging but honest. `"No active scans yet. Create a Signal Subscription to start receiving end-of-day matches."`
- **Legal / onboarding / acknowledgement:** plain, direct, present-tense. `"The platform is in private evaluation and is not registered with SEBI."`
- **Errors:** state what happened and what the user can do. Never blame the user. `"FYERS session expired. Re-authenticate to resume live prices."`

### Emoji

**No decorative emoji.** Icons only (Lucide set) for status, actions, and category markers. A rare unicode arrow (↑ ↓ →) is acceptable in dense tables; never in prose.

### Casing

- Sentence case for all UI labels and buttons: `"Create subscription"`, `"Run backtest"`.
- Title Case reserved for product nouns: `Signal Subscription`, `Risk Management Engine`, `EOD Signal Runner`, `Nifty 500`.
- `ALL CAPS` only for micro-labels (tracked-out, 11px).

### Person

- Address the user as **"you"** and their things as **"your"** (`your portfolio`, `your subscriptions`, `your risk profile`).
- Never use "we" for platform actions — prefer the passive or the system name: *"DataSync completed"*, not *"We synced your data"*.

---

## Visual Foundations

### Palette

Dark-first. The user's request was dark theme, and it matches the category — traders live on dark charting software. A light mode can be derived later but is not in scope.

- **Canvas neutrals** are near-black with a cool, slightly blue undertone (`#07090d` → `#222c3d`), giving five surface elevations without ever turning gray-brown.
- **Brand is a single blue** (`--brand-500: #1f63f5`) — used for focus rings, primary buttons, selected nav, and chart's primary series. Not a gradient. Not a palette of blues used decoratively.
- **Market semantic** is the loudest pair: `--up-500: #1fb85a` and `--down-500: #ef3b3b`. These read AA-compliant on the primary surface. Up/down always get 12% tinted backgrounds (`--up-bg`, `--down-bg`) on pills and cells.
- **Status** is conservative: `--warn-500` for pending / caution only, `--info-500` reuses the brand blue, `--neutral-500` for gray pills.
- **Chart series** is a 6-swatch set for multi-line overlays; the first is brand blue.

### Type

- **Display:** `Space Grotesk 600/700` — for marketing, empty states, onboarding hero. Geometric, slightly distinctive, pairs well with Inter.
- **Sans:** `Inter 400/500/600` — all UI, 14px base.
- **Mono:** `JetBrains Mono 400/500/600` — all numbers (tabular), ticker symbols, codes, timestamps. Every dashboard figure is mono so columns align without hand-kerning.

All three are loaded from Google Fonts. **No substitution flag** — these are commonly-licensed free webfonts.

Weights are restrained: 400 for body, 500 for numbers + medium emphasis, 600 for headings and nav. 700 only for display.

### Spacing

4px base. Components align on an 8px grid. Density is intentional — this is a dense data UI, so 8px / 12px / 16px are the most-used spacings; 24px for section gaps, 32px+ rarely.

### Corners

Deliberately calm. Cards and inputs use `--r-md (8px)` or `--r-lg (12px)`. Pills and chips use `--r-pill`. Nothing uses large pillowy radii; this is not a consumer app.

### Borders & divider strategy

The UI leans on **borders and background elevation** rather than drop shadows. Dark UIs look amateur with heavy shadows; we use:

- `--line-1` for hairline dividers between rows and sections
- `--line-2` on emphasized cards and the selected state
- `--line-3` for focus ring color

Cards: `background: var(--bg-2); border: 1px solid var(--line-1); border-radius: var(--r-lg)`. No shadow unless floating (menu, toast, modal).

### Shadows

Three levels + glow rings. Shadows are **always black**, never colored, because bg is near-black and colored shadows pollute the neutral palette. Focus and selection use ring-shadows instead (`0 0 0 3px rgba(brand, 0.25)`).

### Backgrounds

No full-bleed imagery, no gradients on primary surfaces, no patterns or textures. The chart itself is the most graphical element. Marketing surfaces (if any) may use a **single subtle radial gradient** from brand blue to canvas — one, low opacity, always behind content.

### Animation

Restrained. Financial UI where numbers change should not bounce or overshoot.

- **Durations:** `120ms` (microinteractions), `200ms` (default), `320ms` (panel enter).
- **Easing:** `cubic-bezier(0.22, 1, 0.36, 1)` for out, `(0.65, 0, 0.35, 1)` for in-out. No bounces.
- **Number flashes:** a 400ms tint fade (green or red) when a live price updates — that is the only "delightful" motion.
- **Route transitions:** no. Instant.
- **Skeleton shimmer:** yes, 1.2s linear gradient sweep on loading rows.

### Hover & press states

- **Hover** on a row/card: step up one bg level (`--bg-2` → `--bg-3`), no border change, no scale. Microbuttons: `color` intensifies, `bg` tints `--bg-3`.
- **Press:** step to `--bg-4`, no scale or transform. Never shrink buttons — shrinking feels toy-like in a data UI.
- **Active nav item:** `--bg-4` + 2px brand-500 left border (sidebar) or underline (tabs).
- **Disabled:** `opacity: 0.4`, `cursor: not-allowed`, no hover response.

### Transparency & blur

Sparingly. Modal scrims use `rgba(0,0,0,0.6)` — no blur. Sticky table headers use `backdrop-filter: blur(8px)` with `bg-1 @ 80%` so content scrolls under them legibly. Tooltips are **fully opaque** (`--bg-3`) — a translucent tooltip over a chart is unreadable.

### Cards

`bg-2` surface + `line-1` hairline border + `r-lg` corners + no shadow by default. Emphasized ("selected" or "alerted") card gets a left-edge 2px brand/up/down accent and `line-2` border. Interior padding: `s-5` (20px) horizontal, `s-4` (16px) vertical.

### Layout rules

- **Mobile-first.** Breakpoints: `480` (phone), `768` (tablet), `1024` (laptop), `1440` (desktop). The portal is usable on a phone but optimized for 13–15" laptop screens where traders actually work.
- **Fixed elements:** top app bar (56px), left sidebar on desktop (240px collapsed to 64px), bottom tab bar on mobile (56px). Everything else scrolls.
- **Persistent legal footer** per `REQ-LEGAL-007`: a 32px strip at the bottom of every page with the short-form disclaimer and a link to the full document. It does not collapse on mobile; it shrinks to 2 lines.
- **Content width:** 1280px max, centered. Dashboard uses a 12-column grid; tables and charts stretch full-width.

### Imagery vibe

Minimal imagery overall. When we do show an image (onboarding, marketing), prefer **cool, muted, de-saturated photography** of screens or abstract geometry. No lifestyle photography, no "happy trader on a laptop" stock imagery.

---

## Iconography

Signal Stack uses **[Lucide](https://lucide.dev)** (CDN, default stroke 1.75px, 20px size in UI, 16px in dense tables). Rationale:

- Lucide is clean, technical, and un-branded — doesn't compete with financial data.
- Maintained, CDN-hosted, free, no-attribution.
- Covers every UI need we have (chart, bell, settings, shield, check, x, arrow-up-right…).

**No custom-drawn SVG icons.** If Lucide doesn't have an exact match we pick the closest and document the substitution here (none currently).

- Status dots: small colored circles, 8px. Green = on, amber = warning, red = error, gray = idle.
- Logos and the brand mark are in `assets/` — a stylized "S" stacked bars glyph (primary logo on dark) plus a wordmark lockup.
- **No emoji.**
- **No unicode-as-icon except arrows** (↑ ↓) in dense numeric cells where a Lucide icon would be too heavy.

Icons are always currentColor so they inherit the row/button text color.

---

## Index — what's in this folder

| File / folder | What it is |
|---|---|
| `README.md` | You are here. Brand context, voice, visual foundations. |
| `SKILL.md` | Agent-skill manifest for invoking this system from Claude Code. |
| `colors_and_type.css` | Canonical CSS variables + type classes. Import this first. |
| `assets/` | Logos, brand mark, and key product glyphs. |
| `preview/` | Cards that render in the Design System tab (colors, type, components…). |
| `ui_kits/user-portal/` | Trader portal: dashboard, chart, Signal Builder, notifications. |
| `ui_kits/admin-portal/` | Admin console: approvals, universe, calendar, job control. |

Sample slides are not included — no slide template was provided and the brand is pre-launch.

---

## Caveats

- **No existing product exists yet.** This is a design system for a pre-code product. It is an *informed proposal*, not a codification of an established brand. The user is expected to iterate on palette, type, and component decisions.
- **No logo was provided.** The `assets/` folder contains a simple text-based placeholder wordmark and a geometric glyph built from CSS/SVG shapes. **Please provide a real logo** and we'll swap it in across the system.
- **Dark mode only in scope.** Light mode can be derived by flipping the neutral ramp; not implemented.
- **Icon substitution:** Lucide is used throughout. If you want a different family (Phosphor, Heroicons, Tabler), it's a one-line swap.
- **Chart visuals** are stubbed with SVG — real charts use TradingView lightweight-charts per `CLAUDE.md`. The UI kit shows the chart chrome and panel arrangement, not a real chart renderer.
