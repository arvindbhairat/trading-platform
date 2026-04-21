## infra

Infrastructure definitions and local dev environment support.

Per `docs/repo-structure.md`:
- `docker`: local containers + compose files
- `azure`: Azure deployment definitions, environment templates, pipeline support files

## Local dev dependencies (MongoDB + SQL Server + Redis)

This repo expects local dev instances of:

- **MongoDB** (operational database)
- **SQL Server** (historical candles store)
- **Redis** (cache/queue/leases where needed)

### Prereqs

- Docker Desktop (with the Docker engine running)

### Quickstart (PowerShell)

From the repo root:

```powershell
cd infra\docker
copy .env.example .env
.\up.ps1
```

To stop:

```powershell
cd infra\docker
.\down.ps1
```

### Ports

- **MongoDB**: `mongodb://localhost:27017`
- **SQL Server**: `localhost,1433` (user: `sa`)
- **Redis**: `localhost:6379`

### Notes

- Update `MSSQL_SA_PASSWORD` in `infra/docker/.env` before first run (must meet SQL Server password policy).
- Data is persisted to Docker named volumes so restarts keep state.

