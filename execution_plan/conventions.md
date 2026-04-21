# Conventions (Strict Operational Rules)

This file is the operational contract that enables **incremental execution**, **cross-agent interoperability**, and **deterministic resumption**.

## Single source of truth

- `execution_plan/status.json` is the **only** authoritative state for what has been done and what remains.
- No agent may infer progress from chat history, terminal output, or “seems done” heuristics.

## Start-of-run protocol (mandatory)

Before doing any work:

- Read `execution_plan/status.json`
- Determine the **next task** to execute:
  - If `current_task_id` is set, that is the next task.
  - Otherwise, the next task is the first entry in `pending_tasks`.
- Confirm dependencies:
  - If any dependency is not in `completed_tasks`, do **not** execute the task.
  - Instead set `current_task_id` to the earliest pending dependency (by plan order) and execute that.

## Execution rules (mandatory)

- Execute **only one** task per run.
- Do **not** repeat tasks in `completed_tasks`.
- Do **not** skip tasks unless dependencies are satisfied.
- Do **not** renumber or rename task IDs after v0.1 without an explicit request to re-plan.
- Prefer small vertical slices with demonstrable outcomes (aligns with `docs/implementation-roadmap.md`).

## Update protocol (mandatory)

After completing the task, the agent must update:

- `execution_plan/status.json`
  - Move the task ID from `pending_tasks` → `completed_tasks`
  - Set `current_task_id` to the next pending task (or empty string if none)
  - Update `current_phase` and `current_version` if they changed as a result of completing the task
  - Update `last_updated` (UTC ISO-8601)
- Any relevant implementation artifacts (code/docs/config) produced by the task
- Append a deterministic log entry under `execution_plan/task_logs/`

## Task logs (required)

Each executed task writes one log file with:

- File name format:
  - `YYYYMMDDTHHMMSSZ__{TASK_ID}.md`
- Required sections:
  - Task ID + title
  - Preconditions (dependencies + required env)
  - Actions taken (high-level)
  - Files changed/created (paths)
  - Verification performed (commands/tests or reasoning when tests unavailable)
  - Result (success/failure) + follow-ups (if any)

Logs are append-only; do not edit old logs except to fix factual errors.

## Determinism requirements

- `pending_tasks` ordering must match plan order in `execution_plan.md` (phase order, then task order).
- Never delete task IDs from history.
- If the plan must change:
  - Add new tasks with new IDs at the end of the appropriate phase (or add a new phase).
  - Never reuse or repurpose an existing task ID.
  - Update `execution_plan.md`, `milestones.md`, and `status.json` in the same run as the plan change.

## Cross-agent interoperability rules

- All state transitions must be representable in `status.json` without additional context.
- Tasks must reference canonical docs for requirements/constraints and must not invent new requirements.
- If a task discovers a roadmap/requirements conflict:
  - Stop the task and record the conflict in the task log
  - Create a new task (new ID) to resolve/document the conflict (ADR/doc updates) before continuing

