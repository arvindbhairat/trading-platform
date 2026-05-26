---
name: Hosting — Railway
description: How API, Worker, and Web services are deployed to Railway
---

All three services (API, Worker, Web) are deployed to the **same Railway project**. Build and deployment is handled by **Railway's own GitHub integration** — Railway watches the repo and auto-deploys each service on push to main. No GitHub Actions deployment workflow is needed.

## Services

| Service | Dockerfile | Root Dir | Builder |
|---|---|---|---|
| `api` | `apps/api/Dockerfile` | `.` | Dockerfile |
| `worker` | `apps/worker/Dockerfile` | `.` | Dockerfile |
| `web` | `apps/web/Dockerfile` | `.` | Dockerfile (path: `apps/web/Dockerfile`) |

## Pre-deploy: sys_config seeder

Before Railway deploys, the `sys_config` seeder runs via `.github/workflows/deploy-railway.yml` (manual trigger via `workflow_dispatch`). It seeds MongoDB with required configuration rows. The seeder is a separate .NET console app at `apps/seed/`.

## Web Service

- Next.js 15 app in npm workspaces monorepo (`apps/web`)
- Node.js 24 Alpine Docker image
- Public networking enabled with generated `*.up.railway.app` domain
- `NEXT_PUBLIC_API_BASE_URL` set to Railway API URL
