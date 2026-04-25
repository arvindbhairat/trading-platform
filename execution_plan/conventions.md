# Conventions (Strict Operational Rules)

This file is the operational contract that enables **incremental execution**, **cross-agent interoperability**, and **deterministic resumption**.

## Single source of truth

- `execution_plan/status.json` is the **only** authoritative state for what has been done, what remains, and which REQ-IDs are satisfied per phase.
- No agent may infer progress from chat history, terminal output, or "seems done" heuristics.

## Task definition contract

Every task in `execution_plan/execution_plan.md` carries:

- **Title** — imperative summary.
- **Depends on** — task IDs that must be in `completed_tasks`.
- **REQ** — the REQ-IDs (and ADR / engineering-standards section anchors) the task satisfies.
- **Touches** — the components changed (apps/web, apps/api, apps/worker, packages, infra, docs).
- **Vertical slice** — the smallest end-to-end demonstration of the change, named precisely enough that an agent can verify it.
- **Verification** — how to prove the task is done; must map back to each REQ-ID.
- **Frozen-after-author** *(optional)* — once the original author run completes, the task may not be re-opened in any subsequent phase without an explicit ADR amendment. Originated in `docs/implementation-roadmap.md` § "One-time scaffolding (author once, freeze until phase gate)".
- **active_when** *(optional)* — predicate that must hold for the task to be executed (e.g., a gating ADR result). If false, the task is moved to `deactivated_tasks` per `agent.md` step 3.

## Start-of-run protocol (mandatory)

Before doing any work:

- Read `execution_plan/status.json`.
- Determine the **next task**:
  - If `current_task_id` is set, that is the next task.
  - Otherwise, the next task is the first entry in `pending_tasks`.
- Confirm dependencies:
  - If any dependency is not in `completed_tasks`, do **not** execute the task.
  - Set `current_task_id` to the earliest pending dependency (by plan order) and execute that.
- Evaluate `active_when` if present.
- Refuse to re-execute a Frozen-after-author task that is already in `completed_tasks`.

## Execution rules (mandatory)

- Execute **only one** task per run.
- Do **not** repeat tasks in `completed_tasks`.
- Do **not** skip tasks unless dependencies are satisfied.
- Do **not** renumber or rename task IDs after v0.1 without an explicit re-plan request.
- Prefer small vertical slices with demonstrable outcomes (aligns with `docs/implementation-roadmap.md`).
- Frozen-after-author tasks may only be modified by their original-author run. Subsequent edits require a new ADR amendment task.

## Update protocol (mandatory)

After completing the task, update:

- `execution_plan/status.json`:
  - Move the task ID from `pending_tasks` -> `completed_tasks`.
  - For every REQ-ID satisfied this run, append to `phase_coverage[<phase>]` (de-duplicated).
  - Set `current_task_id` to the next pending task (or empty string if none).
  - Update `current_phase` and `current_version` if they changed as a result of completing the task.
  - Update `last_updated` (UTC ISO-8601).
- Any relevant implementation artefacts (code/docs/config) produced by the task.
- Append a deterministic log entry under `execution_plan/task_logs/`.

## Task logs (required)

Each executed task writes one log file with:

- File name format: `YYYYMMDDTHHMMSSZ__{TASK_ID}.md`.
- Required sections:
  - Task ID + title.
  - Preconditions (dependencies + required env).
  - REQ coverage attempted (REQ-IDs from the task's **REQ** list).
  - Actions taken (high-level).
  - Files changed/created (paths).
  - Verification performed (commands / tests, or reasoning when tests unavailable). Map to each REQ-ID.
  - Frozen-after-author confirmation (if applicable).
  - Conflict found (only if `agent.md` § 6 fired).
  - REQ coverage delta (REQ-IDs satisfied this run / total REQ-IDs scoped to the phase).
  - Result (success/failure) + follow-ups.

Logs are append-only; do not edit old logs except to fix factual errors.

## status.json shape

```
{
  "current_phase":   "P0" | "P1" | ... | "P9",
  "current_version": "v0.1" | ... | "v1.0",
  "current_task_id": "P*-T*" or "",
  "completed_tasks": [ ... ],
  "pending_tasks":   [ ... ],
  "deactivated_tasks": [
    { "id": "P*-T*", "reason": "active_when predicate false", "predicate": "..." }
  ],
  "phase_coverage": {
    "P0": [], "P1": [], "P2": [], "P3": [], "P4": [],
    "P5": [], "P6": [], "P7": [], "P7F": [], "P8": [], "P9": []
  },
  "last_updated": "YYYY-MM-DDTHH:MM:SSZ"
}
```

## Determinism requirements

- `pending_tasks` ordering must match plan order in `execution_plan.md` (phase order, then task order).
- Never delete task IDs from history. Use `deactivated_tasks` to record `active_when` skips.
- If the plan must change:
  - Add new tasks with new IDs at the end of the appropriate phase (or add a new phase).
  - Never reuse or repurpose an existing task ID.
  - Update `execution_plan.md`, `milestones.md`, and `status.json` in the same run as the plan change.

## Cross-agent interoperability rules

- All state transitions must be representable in `status.json` without additional context.
- Tasks must reference canonical docs for requirements/constraints and must not invent new requirements.
- If a task discovers a roadmap/requirements conflict:
  - Stop the task and record the conflict in the task log.
  - Create a new task (new ID) to resolve/document the conflict (ADR/doc updates) before continuing.
  - The new task is appended at the end of the affected phase.
  - The conflicting task remains as `current_task_id` and is gated behind the resolution task.

## Guardrails (carry forward into every task)

These are the non-negotiable guardrails from `AGENTS.md` / `CLAUDE.md`. The agent must verify a task does not violate any of them before marking it complete:

- NSE Nifty 500 universe only; long-only; no autonomous order placement.
- Never use a regular user's FYERS token for shared market-data ingestion.
- All market-data ingestion code routes through the MDP abstraction; no direct provider binding outside adapters.
- Backend services access market and account data exclusively via REST; the FYERS Data WebSocket is browser-tier only.
- Never store user-specific operational state in SQL Server.
- Never blur entry-signal generation and user-initiated execution assistance.
- Server-side validation for auth, Signal, portfolio, and admin-sensitive flows.
- Timeframe logic must not diverge across charting, backtesting, and EOD Signal Runner.
- Startup-critical configuration must not depend solely on MongoDB.
- No secrets in source control, fixtures, or docs.
- Worker Service runs as exactly one instance through Phase A and Phase B (REQ-RME-CONC-006); no auto-scale, no scale-out.
