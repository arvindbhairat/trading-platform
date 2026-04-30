# RUNBOOK — AI Agent Resumption Protocol

For incremental, resumable execution of the repository task pipeline using `execution_plan/agent.md`.

## Quick Start

```markdown
If you are an AI agent:
1. Read this file (RUNBOOK.md) — you are here.
2. Read `CLAUDE.md` for non-negotiable guardrails.
3. Read `execution_plan/agent.md` for the single-task execution procedure.
4. Read `execution_plan/status.json` to identify the next pending task.
5. Execute exactly one task, then update status and write a task log.
```

## Entry Points

| Purpose | File |
|---|---|
| **Agent execution procedure** (required) | `execution_plan/agent.md` |
| **Current task state** (required) | `execution_plan/status.json` |
| **Full task list** | `execution_plan/execution_plan.md` |
| **Operational conventions** | `execution_plan/conventions.md` |
| **Phase acceptance criteria** | `execution_plan/milestones.md` |
| **Task logs** | `execution_plan/task_logs/` |
| **Non-negotiable guardrails** | `CLAUDE.md` (Claude) / `AGENTS.md` (Codex) |

## Canonical Documentation Reference

All product requirements, architecture, and standards live in `docs/`. The authoritative document hierarchy for AI agents:

| Priority | File | Purpose |
|---|---|---|
| 1 | `docs/terminology.md` | Canonical names — read before naming anything |
| 2 | `docs/requirements-spec.md` | Source of truth for all product requirements (REQ-IDs) |
| 3 | `docs/system-architecture.md` | System shape, components, flows, boundaries |
| 4 | `docs/implementation-roadmap.md` | Phase delivery sequence |
| 5 | `docs/engineering-standards.md` | Coding, testing, security standards |
| 6 | `docs/system-config.md` | Runtime config tiers, sys_config seed table |
| 7 | `docs/data-management.md` | MongoDB collection catalogue |
| 8 | `docs/portfolio-risk-guidelines.md` | RME defaults |
| 9 | `docs/project-structure.md` | Practical repo map — where to find/add code |
| 10 | `docs/repo-structure.md` | Canonical monorepo layout rules |
| 11 | `docs/adr/` | Architecture Decision Records |

## Folder Map

```
C:\poc\
├── RUNBOOK.md                    ◄— YOU ARE HERE (agent entry point)
├── CLAUDE.md                     ◄— Guardrails (Claude Code)
├── AGENTS.md                     ◄— Guardrails (Codex/OpenAI)
├── execution_plan/               ◄— Task pipeline (status, plan, logs)
├── apps/
│   ├── api/                      ◄— ASP.NET Core backend (C#)
│   ├── web/                      ◄— Next.js portal (TypeScript)
│   └── worker/                   ◄— .NET background worker (C#)
├── tests/                        ◄— Automated tests (mirrors apps/)
├── packages/                     ◄— Shared packages (planned)
├── infra/                        ◄— Docker + Azure IaC
├── docs/                         ◄— Requirements, architecture, ADRs
├── design_system/                ◄— Brand tokens, UI kit, CSS
└── .claude/skills/               ◄— Domain-specific build instructions
```

See `docs/project-structure.md` for a detailed map with current implementation status and planned layouts.

## One-Task Rule

Execute exactly one task per run. Never batch tasks, never skip ahead. See `execution_plan/conventions.md` § Execution rules.
