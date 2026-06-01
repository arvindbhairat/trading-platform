## infra

Infrastructure definitions and local dev environment support.

Per `docs/repo-structure.md`:
- `docker`: local containers + compose files
- `azure`: Azure deployment definitions, environment templates, pipeline support files

## Local dev dependencies (MongoDB + PostgreSQL + Redis)

This repo expects local dev instances of:

- **MongoDB** (operational database)
- **PostgreSQL** (historical candles store)
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
- **PostgreSQL**: `localhost:5432` (user: `postgres`)
- **Redis**: `localhost:6379`

### Telemetry (OTLP)

Telemetry is exported directly from .NET apps to the configured OTLP endpoint using HTTP/Protobuf.
- Set `Telemetry__Otlp__Endpoint` to the OTLP HTTP endpoint (e.g. `https://api.honeycomb.io`)
- Set `Telemetry__Otlp__ApiKey` to the provider's API key (e.g. Honeycomb Ingest API key)
- When `Endpoint` is empty (local dev default), OTLP export is disabled — logs still go to console via Serilog

### Notes

- Update `POSTGRES_PASSWORD` in `infra/docker/.env` before first run.
- Data is persisted to Docker named volumes so restarts keep state.
