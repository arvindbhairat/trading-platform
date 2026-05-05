# Language Review Checklist

Per `REQ-LEGAL-006`, every user-facing surface must pass a documented language review before release. This checklist defines the review procedure and the prohibited terms.

## Scope

User-facing surface for the purposes of this requirement means:

| Surface | Location |
|---|---|
| Telegram notification content | `apps/api/Notifications/NotificationTemplateBuilder.cs`, `apps/worker/` |
| Portal notification feed entries | `apps/web/src/app/(user)/notifications/` |
| Dashboard labels and helper text | `apps/web/src/app/(user)/` |
| Chart-page advisory panel | `apps/web/src/components/RmeAdvisoryPanel.tsx` |
| Order confirmation modal | `apps/web/src/components/Phase1Modal.tsx` |
| Onboarding screens | `apps/web/src/app/accept-legal/`, `apps/web/src/app/pending-approval/` |
| Any user-visible tooltip, empty state, or confirmation message | All `.tsx` files in `apps/web/src/` |

Internal content not subject to this checklist: internal persistence, logs, database field names, code identifiers, API schema, and canonical terminology (Signal, entry signal, advisory, RME).

## Review Procedure

1. Identify the user-facing surface being added or changed.
2. Read every user-visible string in the change.
3. Verify no prohibited terms appear (see below).
4. Verify the language attributes the decision to the user rather than to the platform.
5. Verify the short-form disclaimer (REQ-LEGAL-007) is present where required.
6. Record the review in the associated task log under **Design system compliance** or **Language review**.

## Prohibited Terms

The following terms must not appear in any user-facing surface:

| Term | Notes |
|---|---|
| `buy` | Use "open position" or factual reporting ("entry filled") instead |
| `sell` | Use "close position" or "reduce position" instead |
| `recommended` | Use "configured," "set," or "your criteria" instead |
| `suggested pick` | Platform does not make picks |
| `top pick` | Platform does not rank or pick securities |
| `advised` | Use "configured" or "per your settings" instead |
| `signal to buy` / `signal to sell` | Use "your scan matched" or "your entry criteria were met" instead |
| Any equivalent phrasing that attributes a recommendation or opinion to the platform | — |

## Acceptable Language Patterns

These patterns are compliant with REQ-LEGAL-006:

| ✅ Compliant | ❌ Non-compliant |
|---|---|
| "Open position" | "Buy" |
| "Close position" | "Sell this stock" |
| "Your scan matched {symbol}" | "We recommend {symbol}" |
| "Your configured entry is..." | "Top pick" |
| "Your stop per your risk profile..." | "You should buy" |
| "This symbol met the criteria you set..." | "Signal to buy" |
| "You are the sole decision-maker" | "Our advised action is..." |
| "Entry filled — {symbol}" (factual) | — |

## Per-Component Review Records

Each user-facing component or notification template must have a review record. The record is stored as a section in the associated task log.

| Component / Template | Last Reviewed | Reviewer | Pass |
|---|---|---|---|
| `NotificationTemplateBuilder.cs` (entry-fill) | P6-T29 | Agent | ✓ |
| `NotificationTemplateBuilder.cs` (exit-advisory) | P6-T29 | Agent | ✓ |
| `RmeAdvisoryPanel.tsx` | P6-T27 | Agent | ✓ |
| `Phase1Modal.tsx` | P7-T4 | Agent | ✓ |
| Notification templates under `apps/worker/` | P8-T15 | Agent | ✓ |

## CI Enforcement

The CI pipeline includes a `language-review-lint` job (defined in `.github/workflows/ci.yml`) that scans all user-facing source files for prohibited terms. The job fails the build if any prohibited term is detected. See `scripts/language-review-lint.sh`.
