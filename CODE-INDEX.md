# Code Index System

This file documents how AI agents discover, navigate, and reference code in this repository. It is designed for two modes of operation:

1. **Build phase** — when agents execute tasks from `execution_plan/agent.md`
2. **Live trials** — when agents fix bugs or issues reported after the build is complete

## How it works

There are no generated index files, no scheduled scripts, and no manual maintenance. The system uses **agent-driven on-the-fly indexing** powered by the MCP tools already available in the agent environment (Glob, Grep, and the Explore agent).

Your existing artefacts do the heavy lifting:

| What | How the agent finds it |
|------|----------------------|
| **All source files** | `Glob` at session start — zero-maintenance file inventory |
| **REQ-ID to file mapping** | `Grep` across `execution_plan/task_logs/*.md` — every task log records `Files changed / created` and `REQ coverage` |
| **Component relationships** | `Explore` agent or targeted Grep queries |
| **Architecture decisions** | `docs/adr/` — read the relevant ADR |
| **Domain guidance** | `.claude/skills/{domain}.md` — read the matching skill |

## During build phase

Build-phase code discovery is integrated into `execution_plan/agent.md`. At every session start, step 1 now includes:

```
- Run code discovery to load the full source inventory
```

This costs ~7 Glob calls per session and gives the agent a complete file map for the entire run.

## During live trials (bug fixes)

The bug-fix protocol is documented in `BUG-FIX-PROTOCOL.md` (root). An agent fixing a live-trial issue follows these steps:

1. Load the file inventory via Glob.
2. If a REQ-ID is known, Grep the task logs to find the affected files.
3. If no REQ-ID is known, Grep the source tree by error message or component name.
4. Read the relevant skill file and canonical docs.
5. Read the affected files, diagnose, fix, and verify.
6. Write a bug-fix task log (which feeds back into the REQ-to-file map for future sessions).

## Finding the REQ-ID cross-reference

There is no single file listing REQ-IDs and their files. Instead, query the task logs:

```
Grep("REQ-<ID>", "execution_plan/task_logs/", output_mode: "files_with_matches")
```

This returns the task log files that reference the REQ-ID. Read any of them to find the exact `Files changed / created` section listing every file touched for that requirement.

**Why this approach instead of a generated file**: The task logs are always up-to-date (they are written by every completed task), and Grep is fast. A generated cross-reference file would drift between script runs; this approach does not.

## Quick reference for agents

### File inventory (run at session start)

```
Glob("apps/**/*.cs")              # all .NET source files (api, worker, seed)
Glob("apps/web/src/**/*.ts*")     # all TypeScript/TSX source files
Glob("apps/web/*.ts")             # root-level TS files (next.config.ts, middleware.ts)
Glob("apps/web/__tests__/**/*.ts*") # Next.js test files
Glob("packages/**/*.cs")          # all shared package source
Glob("tests/**/*.cs")             # all .NET test files
Glob("tests/**/*.ts*")            # all TypeScript test files
```

### REQ-ID lookup

```
Grep("REQ-<ID>", "execution_plan/task_logs/", output_mode: "files_with_matches")
```

Then read the resulting task log to find the exact file list.

### Surface-to-skill mapping

| Surface | Skill file | Key doc |
|---------|-----------|---------|
| API / domain logic | `.claude/skills/backend-foundation.md` | `docs/system-architecture.md` |
| Portal / chart / UI | `.claude/skills/frontend-trading-ui.md` | `docs/design-system/SKILL.md` |
| Market data / FYERS | `.claude/skills/market-data-and-orders.md` | `docs/requirements-spec.md` |
| Strategy / backtest | `.claude/skills/strategy-and-backtesting.md` | `docs/requirements-spec.md` |
| Auth / integrations | `.claude/skills/identity-and-integrations.md` | `docs/requirements-spec.md` |
| Admin / operations | `.claude/skills/admin-and-operations.md` | `docs/system-config.md` |
| RME / risk | `.claude/skills/risk-and-compliance.md` | `docs/portfolio-risk-guidelines.md` |
| Testing / CI | `.claude/skills/qa-and-release.md` | `docs/engineering-standards.md` |

### Problem-solving flow

```
Bug report → identify surface + REQ-ID → Glob file inventory
  → Grep task logs for REQ-ID → read affected files
  → read skill file + canonical doc → diagnose → fix → write task log
```

## Benefits of this approach

- **Zero maintenance** — no scripts to run, no files to regenerate
- **Always up-to-date** — reads the live filesystem and the authoritative task logs
- **Works in Cowork and Claude Code** — uses only MCP tools (Glob, Grep, Explore agent)
- **Self-sustaining** — each bug fix writes a task log, enriching the REQ-to-file map for future sessions
- **No environment dependencies** — no Node.js, no PowerShell, no build step
