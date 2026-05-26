#!/bin/sh
set -e

# Write runtime API config before starting Next.js.
# This bypasses Next.js build-time env var inlining — Railway injects
# these vars at container runtime, not during the Docker build.
API_BASE_URL="${API_BASE_URL:-}"
echo "{\"apiBaseUrl\":\"${API_BASE_URL}\"}" > /app/public/api-config.json
echo "[entrypoint] wrote apiBaseUrl=${API_BASE_URL:-"(empty)"}"

exec npx next start
