# Agent Continuation Runbook (Single-Line Resume Target)

If you are an AI agent and the user says **“Continue execution using `execution_plan/agent.md`”**, you must follow this file verbatim.

## Prime directive

Execute **exactly one** next pending task from `execution_plan/status.json`, without inventing scope, and update tracking so another agent can resume deterministically.

## Canonical references (must respect)

- `docs/requirements-spec.md` is the source of truth for requirements.
- `docs/implementation-roadmap.md` sequences delivery (do not invent new requirements).
- `docs/engineering-standards.md` defines reusable engineering constraints.
- `execution_plan/execution_plan.md` defines the task list, order, dependencies, and expected outcomes.
- `execution_plan/conventions.md` defines strict operating rules for resumption and logging.

## Step-by-step procedure (do this in order)

### 1) Load state (required)

- Read `execution_plan/status.json`.
- Let:
  - `nextTask = current_task_id` if non-empty, else first entry in `pending_tasks`.
- If `nextTask` is empty and `pending_tasks` is empty:
  - Stop: nothing to do; report completion.

### 2) Validate dependencies (required)

- Look up `nextTask` in `execution_plan/execution_plan.md` to find its **dependencies**.
- If any dependency is not in `completed_tasks`:
  - Set `nextTask` to the earliest missing dependency that is still pending (by plan order).
  - Do **not** execute the originally selected task.

### 3) Execute only `nextTask` (required)

- Perform the minimum code/docs/config changes necessary to satisfy the task’s expected outcomes.
- Keep the work **atomic** and **idempotent**:
  - Prefer adding new artifacts over rewriting existing ones.
  - Do not reset progress or regenerate the plan unless explicitly asked.

### 4) Verify (required)

- Run the smallest reasonable verification for the task:
  - lint/build/test commands if available and fast, or
  - reasoned verification if runtime dependencies are unavailable in the environment.
- Capture what you verified in the task log (below).

### 5) Write the task log (required)

- Create exactly one log file under `execution_plan/task_logs/`:
  - Name: `YYYYMMDDTHHMMSSZ__{TASK_ID}.md`
  - Include sections:
    - Task ID + title
    - Preconditions (deps + environment assumptions)
    - Actions taken
    - Files changed/created
    - Verification performed
    - Result + follow-ups

### 6) Update `status.json` (required)

Update the state **without losing history**:

- Append `nextTask` to `completed_tasks` (if not already present).
- Remove `nextTask` from `pending_tasks`.
- Set:
  - `current_task_id` to the first remaining entry in `pending_tasks` (or `""` if none).
  - `current_phase` and `current_version` to match the new `current_task_id` (use the mapping from `execution_plan/execution_plan.md` + `execution_plan/milestones.md`).
  - `last_updated` to current UTC time (ISO-8601, e.g. `2026-04-21T13:45:12Z`).

### 7) Output to user (required)

Report:

- The executed task ID + title
- What changed (high-signal)
- Where the log is
- What the next task is (from updated `status.json`)

## Hard prohibitions

- Do not execute more than one task per run.
- Do not mark tasks complete without producing the expected outcomes and a task log.
- Do not “skip ahead” based on intuition; dependencies must be satisfied.
- Do not rewrite `execution_plan/execution_plan.md` wholesale.

