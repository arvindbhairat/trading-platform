$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath ".\.env")) {
  throw "Missing .env. Create it from .env.example (copy .env.example .env) and set POSTGRES_PASSWORD."
}

docker compose --env-file .env -f docker-compose.yml up -d
docker compose -f docker-compose.yml ps
