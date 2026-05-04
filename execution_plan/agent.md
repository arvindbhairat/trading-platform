# Agent Continuation Runbook (Single-Line Resume Target)

If you are an AI agent and the user says **"Continue execution using `execution_plan/agent.md`"** (or **"Continue using `RUNBOOK.md`"**), you must follow this file verbatim.

## Prime directive

Execute **exactly one** next pending task from `execution_plan/status.json`, without inventing scope, and update tracking so another agent can resume deterministically.

## Canonical references (must respect)

- `docs/requirements-spec.md` — source of truth for product requirements. **Cite REQ-IDs.**
- `docs/system-architecture.md` — architecture explanation; system boundaries.
- `docs/implementation-roadmap.md` — phased delivery sequence (do not invent new requirements).
- `docs/engineering-standards.md` — reusable engineering and testing standards, including mandatory Design System Standards (§ "Design System Standards").
- `design_system/` — canonical UI source of truth: tokens (`colors_and_type.css`), mock screens (`mock_screens/`), UI kits (`ui_kits/`), preview files (`preview/`).
- `docs/system-config.md` — runtime configuration tiers and `sys_config` seed table.
- `docs/data-management.md` — MongoDB collection catalogue, TTL/Online Archive.
- `docs/portfolio-risk-guidelines.md` — RME defaults.
- `docs/terminology.md` — canonical names and abbreviations.
- `docs/repo-structure.md` — preferred monorepo layout.
- `docs/adr/` — accepted decisions; respect ADR-0001..0005 boundaries.
- `AGENTS.md` / `CLAUDE.md` — non-negotiable guardrails.
- `execution_plan/execution_plan.md` — the task list, dependencies, REQ coverage per task, vertical slices, Frozen-after-author markers.
- `execution_plan/conventions.md` — strict operating rules.
- `execution_plan/milestones.md` — version checkpoints and phase exit-readiness criteria.

If a task surfaces a contradiction with the canonical docs, do not patch it in place. Stop the task and create a conflict-resolution task per § 6 below.

## Task definition contract (read from `execution_plan.md`)

Each task carries:

- **Title** — imperative summary.
- **Depends on** — task IDs that must be in `completed_tasks` first.
- **REQ** — REQ-IDs (and ADR / engineering-standards section anchors) the task satisfies.
- **Touches** — components changed (apps/web, apps/api, apps/worker, packages, infra, docs).
- **Vertical slice** — the smallest end-to-end demonstration of the change.
- **Verification** — how to prove the task is done.
- **Frozen-after-author** (when present) — the task must not be re-opened in any subsequent phase without an explicit ADR amendment. Originated in `docs/implementation-roadmap.md` § "One-time scaffolding (author once, freeze until phase gate)".
- **active_when** (when present) — a predicate (e.g., "REQ-ORDER-010b verification recorded as `pass`") that must hold for the task to be executed.

## Step-by-step procedure (do this in order)

### 1) Load state and discover code (required)

- Read `execution_plan/status.json`.
- Let `nextTask = current_task_id` if non-empty, else first entry in `pending_tasks`.
- If both are empty: stop and report completion.
- Run code discovery: use Glob to scan source directories so the agent has the full file inventory for this session. This costs ~7 Glob calls and eliminates "which file is that in" during the task.
  ```
  Glob("apps/**/*.cs")                 # all .NET source files
  Glob("apps/web/src/**/*.ts*")        # frontend source files
  Glob("apps/web/*.ts")                # root-level TS (next.config.ts, middleware.ts)
  Glob("apps/web/__tests__/**/*.ts*")  # frontend test files
  Glob("packages/**/*.cs")             # shared .NET packages
  Glob("tests/**/*.cs")                # .NET test files
  Glob("tests/**/*.ts*")               # TypeScript test files
  ```

### 2) Validate dependencies (required)

- Look up `nextTask` in `execution_plan/execution_plan.md` to find its **Depends on** list.
- If any dependency is not in `completed_tasks`, set `nextTask` to the earliest missing dependency that is still pending (by plan order).
- Do **not** execute the originally selected task.

### 3) Evaluate `active_when` (required)

- If the task carries an `active_when` predicate, evaluate it against the current state (most often an ADR file under `docs/adr/`).
- If the predicate is false, mark the task `deleted-by-branch` in `status.json` (move it to the `deactivated_tasks` list with the reason and the predicate value), advance to the next pending task, and re-enter step 1.

### 4) Honour Frozen-after-author (required)

- If the task is marked **Frozen-after-author** and is already in `completed_tasks`, refuse to re-execute it. The only legal change is via a new task that records an ADR amendment first.

### 5) Execute only `nextTask` (required)

- Perform the minimum code / docs / config changes necessary to satisfy the task's **Vertical slice** and the REQ-IDs listed under **REQ**.
- Keep work atomic and idempotent. Prefer adding new artefacts over rewriting existing ones (except where the task is explicitly a rewrite).
- **Design gate for UI work.** If the task touches `apps/web` or any frontend component, before writing any code:
  1. Read the corresponding mock screen in `design_system/mock_screens/` for the feature being built
  2. Read the relevant UI kit components in `design_system/ui_kits/` for the portal being built (user-portal or admin-portal)
  3. Map required components from `apps/web/src/components/primitives.tsx` — these are the production TypeScript mirror of the design system primitives; do not copy from `design_system/mock_screens/primitives.jsx` directly without converting
  4. Use only design tokens from `apps/web/src/app/globals.css` — spacing (`var(--s-*)`), colors (`--bg-*`/`--fg-*`/`--brand-*`/`--up-*`/`--down-*`/`--warn-*`/`--info-*`/`--neutral-*`/`--line-*`), type classes (`t-h1`–`t-label-sm`, `t-body-*`, `t-num-*`), radii (`var(--r-*)`)
  5. Validate layout and hierarchy against `design_system/preview/` reference HTML files before finalising
  6. Document which mock screen was consulted in the task log under **Design system compliance**
  Do not introduce hardcoded colors, spacing, font sizes, shadows, or inline styles with arbitrary values.
- Do not regenerate the plan; do not reset progress.

### 6) Conflict handling (required)

If, while executing, you discover any of the following:

- a missing REQ that the task implicitly depends on,
- a contradiction between the task and a canonical doc,
- a guardrail violation that the task as written would require,

then:

1. Stop the task. Do not patch the conflict in place.
2. Record the conflict in the task log (see step 8) under a section titled **Conflict found**.
3. Create a new conflict-resolution task with a new ID at the end of the affected phase. Its purpose is to update the canonical doc (or add an ADR) and then unblock this task.
4. Add the new task to `pending_tasks` and to `execution_plan.md`.
5. Update `status.json` to leave the original task as `current_task_id` and the new conflict-resolution task as the next dependency.
6. Output the conflict to the user and exit.

### 7) Verify (required)

- Run the smallest reasonable verification for the task: lint / build / test commands if available and fast, or reasoned verification if the runtime dependency is unavailable in the environment.
- **Design compliance (UI tasks).** If the task touched `apps/web` or any frontend component:
  - Grep the changed files for hardcoded style violations: `color: "#`, `padding: "`, `font-size:`, `margin: "` — confirm zero new violations
  - Confirm all new UI components extend or compose from `src/components/primitives.tsx` rather than duplicating existing primitives
  - Confirm the visual output matches the corresponding `design_system/mock_screens/` reference in layout, spacing, and hierarchy
  - Record the mock screen consulted and the compliance check result in the task log under **Design system compliance**
- For each REQ-ID listed under **REQ**, assert that it is observably true (the test, the code path, the config row, the migration — whatever the task scope demands).
- Record what you verified per REQ-ID in the task log.

### 8) Write the task log (required)

- One file under `execution_plan/task_logs/`.
- Name: `YYYYMMDDTHHMMSSZ__{TASK_ID}.md`.
- Required sections:
  - Task ID + title.
  - Preconditions (dependencies + environment assumptions).
  - REQ coverage attempted (the REQ-IDs from the task's **REQ** list).
  - Actions taken.
  - Files changed / created (paths).
  - Verification performed (commands / tests, or reasoning when tests unavailable). Map to each REQ-ID.
  - Design system compliance (mock screen consulted, violations found, if UI task).
  - Frozen-after-author confirmation (if applicable).
  - Conflict found (only if step 6 fired).
  - REQ coverage delta (REQ-IDs satisfied this run / total REQ-IDs scoped to the phase).
  - Result + follow-ups.

### 9) Update `status.json` (required)

Update the state without losing history:

- Append `nextTask` to `completed_tasks` (if not already present).
- Remove `nextTask` from `pending_tasks`.
- For each REQ-ID satisfied this run, append it to `phase_coverage[<phase>]` (de-duplicated).
- Set `current_task_id` to the first remaining entry in `pending_tasks` (or `""` if none).
- Set `current_phase` and `current_version` to match the new `current_task_id` (use the mapping in `execution_plan.md` + `milestones.md`).
- Set `last_updated` to the current UTC time (ISO-8601, e.g., `2026-04-25T13:45:12Z`).

### 10) Phase-gate verification (required if last task in phase)

If the just-completed task is the last task in its phase **and** the phase has a phase-gate verification task (named `P*-T*` with title beginning "Phase-gate verification"):

- Assert that every REQ-ID scoped to the phase (per `milestones.md` acceptance criteria) appears in `status.json.phase_coverage[<phase>]`.
- Assert that no Frozen-after-author task in the phase has been re-opened by a later run.
- Assert that the phase's exit-readiness criteria in `milestones.md` hold.
- If pass, mark the phase-gate task complete and advance `current_phase` / `current_version`.
- If fail, create a remediation task at the end of the phase, leave it as the next pending task, and stop.

### 11) Output to user (required)

Report:

- The executed task ID + title.
- What changed (high-signal).
- REQ coverage delta — REQ-IDs satisfied this run, and the running phase coverage (`X / Y` REQ-IDs satisfied for `<phase>`).
- Where the log is.
- What the next task is (from updated `status.json`).

## Hard prohibitions

- Do not execute more than one task per run.
- Do not mark a task complete without producing the **Vertical slice** outcome and a task log.
- Do not "skip ahead" based on intuition; dependencies must be satisfied.
- Do not re-open a Frozen-after-author task except via an explicit ADR amendment task.
- Do not patch a conflict in place; always create a conflict-resolution task per step 6.
- Do not rewrite `execution_plan.md`, `milestones.md`, `agent.md`, or `conventions.md` wholesale.
- Do not introduce work that violates the non-negotiable guardrails in `AGENTS.md` / `CLAUDE.md`.
