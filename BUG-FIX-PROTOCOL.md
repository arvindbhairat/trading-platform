# Bug-Fix Protocol (Live Trials & Build Phase)

Use this guide when a bug, issue, or unexpected behaviour is reported during live trials (post-build) or discovered during the current build phase. It prescribes an agent-driven code discovery workflow that uses existing MCP tools — no generated index files, no scripts to run.

## Reference Docs

| Document | When to read |
|---|---|
| `docs/requirements-spec.md` | Any REQ-ID mentioned in the bug |
| `docs/system-architecture.md` | Understanding component boundaries |
| `docs/terminology.md` | Before naming anything new |
| `docs/data-management.md` | Collection catalogue, TTL rules |
| `docs/engineering-standards.md` | Coding and testing conventions |
| `docs/repo-structure.md` | Preferred layout rules |
| `docs/project-structure.md` | Navigable map of existing code |
| `.claude/skills/{domain}.md` | Domain-specific build instructions |
| `CODE-INDEX.md` | Quick-reference for discovery commands |
| `execution_plan/agent.md` | Task-execution runbook (build-phase fixes) |

## Protocol

### Step 1 — Load the context

- Read the bug report or issue description.
- Identify the affected surface: API, portal, worker job, or data.
- If a REQ-ID is mentioned, note it — it is the strongest signal to affected files.
- If no REQ-ID is given, identify one from the surface area by scanning `docs/requirements-spec.md` or asking the reporter.

### Step 2 — Build the file inventory

Run these tool calls at session start. This gives the agent a complete file map in ~5 calls:

```
Glob("apps/**/*.cs")                 # all .NET source files
Glob("apps/web/src/**/*.ts*")        # frontend source files
Glob("apps/web/*.ts")                # root-level TS (next.config.ts, middleware.ts)
Glob("apps/web/__tests__/**/*.ts*")  # frontend test files
Glob("packages/**/*.cs")             # shared .NET packages
Glob("tests/**/*.cs")                # .NET test files
Glob("tests/**/*.ts*")               # TypeScript test files
```

### Step 3 — Find the affected files

**If a REQ-ID is known:**
Grep the task logs to find which task files reference the REQ-ID:

```
Grep("REQ-<ID>", "execution_plan/task_logs/", output_mode: "files_with_matches")
```

Read the matching task log(s) — each contains a `Files changed / created` section listing every file touched for that requirement. This is the authoritative map from requirement to code.

**If no REQ-ID is known (runtime error):**
Grep the source tree by error message or component name:

```
Grep("<error-text-or-component-name>", "apps/")
```

**If the question is structural ("where does the EOD sync start?"):**
Use the Explore agent type:

```
Agent({
  subagent_type: "Explore",
  description: "Find EOD sync entry point",
  prompt: "Find the entry point for the EOD DataSync job in apps/worker. I need: (1) the job class, (2) its scheduler registration, (3) any configuration keys it reads, (4) the data store it writes to, and (5) its test file."
})
```

### Step 4 — Read domain context

Read the skill file matching the affected surface:

| Surface | Skill file |
|---|---|
| API / domain | `.claude/skills/backend-foundation.md` |
| Portal / chart / UI | `.claude/skills/frontend-trading-ui.md` |
| Market data / FYERS | `.claude/skills/market-data-and-orders.md` |
| Strategy / backtest | `.claude/skills/strategy-and-backtesting.md` |
| Auth / integrations | `.claude/skills/identity-and-integrations.md` |
| Admin / operations | `.claude/skills/admin-and-operations.md` |
| RME / risk | `.claude/skills/risk-and-compliance.md` |
| Testing / CI | `.claude/skills/qa-and-release.md` |

### Step 5 — Diagnose

- Read the affected files (identified in step 3).
- Trace the flow: entry point → service → data access → response.
- Check test files from the inventory for existing coverage.
- Check config in `docs/system-config.md`.
- Formulate a hypothesis.

### Step 6 — Fix

- Make the minimal change that satisfies the requirement.
- Keep the fix atomic and idempotent.
- Add or update tests.
- Verify against the relevant REQ-IDs.

### Step 7 — Log

Write a bug-fix task log to `execution_plan/task_logs/` so the REQ-to-file map stays current:

```
YYYYMMDDTHHMMSSZ__BUG-<short-description>.md
```

Required sections:
- **Bug description** — what was observed vs. expected
- **REQ coverage** — which REQ-IDs were affected
- **Root cause** — what was wrong
- **Fix applied** — what changed and why
- **Files changed / created** — full paths
- **Verification performed** — tests run, manual checks
- **REQ coverage delta** — REQ-IDs satisfied this run

## During build phase

If this bug is discovered during active build-phase execution, use `execution_plan/agent.md` § 6 (Conflict handling):

1. Stop the current task.
2. Record the conflict in the task log.
3. Execute the fix as a new task appended to the current phase.
4. Add the new task to `execution_plan/execution_plan.md` and `status.json`.
5. Resume the original task after the fix task completes.

The code discovery and REQ lookup steps above apply identically. The only difference is that the fix is tracked in the execution plan rather than handled ad-hoc.
