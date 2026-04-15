# Short-Form Disclaimer Strings

**Version:** v1
**Phase:** A
**Status:** In-house draft. Subject to legal review before Phase B.

---

## Purpose

Short, surface-appropriate disclaimer text that must appear on every user-facing surface where the platform delivers actionable content, per `REQ-LEGAL-007`. These strings are seeded into `sys_config` under the `legal` category so the admin can update them without a code deployment. Every change to these strings must be recorded in `audit_events`.

All four strings share the same core message: the platform produces information derived from the user's own configured scans, the user is the decision-maker, and trading carries risk of loss. Each variant is sized to the context it appears in.

Internal copy (logs, APIs, canonical terminology) is unaffected by these strings; this is user-facing content only, per `REQ-LEGAL-006`.

---

## Variants

### Telegram notification footer

Appears as the final line of every Telegram message sent by the platform — entry signal, exit alert, add advisory, reduce advisory, drawdown alert, etc.

> Based on your scan config. Not advice. You decide. Trading carries risk of loss.

- **sys_config key:** `legal.disclaimer.short_text` (variant identifier: `telegram`)
- **Character budget:** ~80 characters to fit gracefully under each notification body.

### Portal dashboard persistent footer strip

Thin strip pinned to the bottom of the dashboard viewport. Visible at all times while the user is on the dashboard.

> This platform is a tool for your own decision-making. It is not registered with SEBI and does not provide investment advice. Every trade is your decision. Markets carry risk of loss.

- **sys_config key:** `legal.disclaimer.short_text` (variant identifier: `dashboard_strip`)
- **Character budget:** ~180 characters, single line on desktop, wraps on mobile.

### Chart page advisory panel inline strip

Displayed inside the RME advisory panel on the chart page, directly above or beneath the advisory values.

> These advisory values are derived from the parameters in your Signal Subscription. They are not investment advice. The decision to act is yours.

- **sys_config key:** `legal.disclaimer.short_text` (variant identifier: `chart_advisory`)
- **Character budget:** ~160 characters, fits within the advisory panel width.

### Phase 1 order confirmation modal (above the confirm button)

Prominent strip positioned directly above the confirm button in the order placement modal. Must be visually distinct from informational warnings so the user cannot mistake it for an automated recommendation.

> You are placing this order on your own decision. The platform provides informational tools only; it does not recommend this trade. Trading carries risk of loss, including the full amount of this order.

- **sys_config key:** `legal.disclaimer.short_text` (variant identifier: `order_modal`)
- **Character budget:** ~200 characters, two lines on desktop, may wrap further on small screens.

---

## Storage Model

The seed table in `docs/system-config.md` lists a single `legal.disclaimer.short_text` key. In implementation, this key must hold a small JSON object keyed by variant identifier:

```json
{
  "telegram": "Based on your scan config. Not advice. You decide. Trading carries risk of loss.",
  "dashboard_strip": "This platform is a tool for your own decision-making. It is not registered with SEBI and does not provide investment advice. Every trade is your decision. Markets carry risk of loss.",
  "chart_advisory": "These advisory values are derived from the parameters in your Signal Subscription. They are not investment advice. The decision to act is yours.",
  "order_modal": "You are placing this order on your own decision. The platform provides informational tools only; it does not recommend this trade. Trading carries risk of loss, including the full amount of this order."
}
```

The `valueType` for this sys_config entry is therefore `json`. Each surface must read its own variant and must not substitute a different variant's text.

---

## Long-Form Disclaimer Link

Every surface that displays a short-form disclaimer must include a link to the long-form disclaimer document. The URL is sourced from `sys_config` under `legal.disclaimer.long_url` and the displayed version is sourced from `legal.disclaimer.long_version`. Link label:

> Read the full disclaimer

---

## Change Log

- **v1** — Initial in-house draft. Calibrated to reinforce self-directed framing on every surface while staying short enough to be read, not skipped.
