## infra

Infrastructure definitions and local dev environment support.

Per `docs/repo-structure.md`:
- `docker`: local containers + compose files
- `azure`: Azure deployment definitions, environment templates, pipeline support files

## Local dev dependencies (MongoDB + SQL Server + Redis + OTLP collectors)

This repo expects local dev instances of:

- **OpenTelemetry collectors** (primary + fallback OTLP endpoints)
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

- **OTLP primary collector (gRPC/HTTP)**: `localhost:4317` / `localhost:4318`
- **OTLP fallback collector (gRPC/HTTP)**: `localhost:14317` / `localhost:14318`
- **MongoDB**: `mongodb://localhost:27017`
- **SQL Server**: `localhost,1433` (user: `sa`)
- **Redis**: `localhost:6379`

### Notes

- Update `MSSQL_SA_PASSWORD` in `infra/docker/.env` before first run (must meet SQL Server password policy).
- Data is persisted to Docker named volumes so restarts keep state.
- API and Worker default to the primary OTLP endpoint via `Telemetry:Otlp:PrimaryEndpoint`.
- To reroute telemetry without code changes, set `Telemetry__Otlp__UseFallbackEndpoint=true` or point `Telemetry__Otlp__PrimaryEndpoint` directly at the fallback collector.

