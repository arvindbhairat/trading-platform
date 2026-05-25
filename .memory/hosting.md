---
name: Hosting — Railway
description: How API, Worker, and Web services are deployed to Railway
---

All three services (API, Worker, Web) are deployed to the **same Railway project** via `.github/workflows/deploy-railway.yml`.

## Services

| Service | Dockerfile | Root Dir | Builder |
|---|---|---|---|
| `api` | `apps/api/Dockerfile` | `.` | Dockerfile |
| `worker` | `apps/worker/Dockerfile` | `.` | Dockerfile |
| `web` | `apps/web/Dockerfile` | `.` | Dockerfile (path: `apps/web/Dockerfile`) |

## Deploy Order (GitHub Actions)

1. `seeder` — runs sys_config seed against MongoDB
2. `deploy-api` — deploys API service
3. `deploy-web` + `deploy-worker` — run in parallel after API is deployed
4. `health-check` — verifies API health endpoint

## Web Service

- Next.js 15 app in npm workspaces monorepo (`apps/web`)
- Node.js 24 Alpine Docker image
- Public networking enabled with generated `*.up.railway.app` domain
- `NEXT_PUBLIC_API_BASE_URL` set to Railway API URL
