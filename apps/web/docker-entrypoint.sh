#!/bin/sh
set -e

# Write runtime API config before starting Next.js.
# Tries multiple env var names that may be set on Railway.
# Railway injects vars at container runtime; this bypasses Next.js
# build-time process.env inlining.

echo "[entrypoint] DEBUG: API_BASE_URL=${API_BASE_URL:-<unset>}"
echo "[entrypoint] DEBUG: API_BACKEND_URL=${API_BACKEND_URL:-<unset>}"
echo "[entrypoint] DEBUG: NEXT_PUBLIC_API_BASE_URL=${NEXT_PUBLIC_API_BASE_URL:-<unset>}"

# Pick the first non-empty value across all known var names
API_BASE_URL="${API_BASE_URL:-${API_BACKEND_URL:-${NEXT_PUBLIC_API_BASE_URL:-}}}"

echo "{\"apiBaseUrl\":\"${API_BASE_URL}\"}" > /app/public/api-config.json
echo "[entrypoint] wrote apiBaseUrl=${API_BASE_URL:-"(empty)"}"

exec npx next start
