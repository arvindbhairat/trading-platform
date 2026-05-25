---
name: Railway environment variables per service
description: Tracks which env vars are configured on each Railway service (api, worker, web) and which are still pending.
type: reference
---

## API Service

| Variable | Status |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | ✅ Configured |
| `ConnectionStrings__MongoDb` | ✅ Configured |
| `ConnectionStrings__Redis` | ✅ Configured |
| `ConnectionStrings__SqlServer` | ✅ Configured |
| `Telemetry__Otlp__Endpoint` | ✅ Configured |
| `Telemetry__Otlp__ApiKey` | ✅ Configured |
| `Auth__Jwt__Secret` | ✅ Configured |
| `FYERS_APP_ID` | ✅ Configured |
| `FYERS_APP_SECRET` | ✅ Configured |
| `Auth__FrontendBaseUrl` | ✅ Configured |
| `Auth__Google__ClientId` | ✅ Configured |
| `Auth__Google__ClientSecret` | ✅ Configured |
| `Auth__Microsoft__ClientId` | ❌ Not yet configured |
| `Auth__Microsoft__ClientSecret` | ❌ Not yet configured |
| `Auth__Facebook__AppId` | ❌ Not yet configured |
| `Auth__Facebook__AppSecret` | ❌ Not yet configured |
| `Auth__SeedAdminEmail` | ✅ Configured — email of the platform admin; used by `AuthEndpoints.cs` to upsert admin role on sign-in |
| `FYERS_ACCESS_TOKEN` | ❌ Not configured — expected to be generated per-user after Fyers OAuth authentication |

## Worker Service

| Variable | Status |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | ✅ Configured |
| `ConnectionStrings__MongoDb` | ✅ Configured |
| `ConnectionStrings__Redis` | ✅ Configured |
| `ConnectionStrings__SqlServer` | ✅ Configured |
| `Telemetry__Otlp__Endpoint` | ✅ Configured |
| `Telemetry__Otlp__ApiKey` | ✅ Configured |
| `TELEGRAM_BOT_TOKEN` | ✅ Configured |
| `FYERS_APP_ID` | ✅ Configured |
| `FYERS_ACCESS_TOKEN` | ❌ Not configured — expected to be generated per-user after Fyers OAuth authentication |

## Web Service

| Variable | Status |
|---|---|
| `NEXT_PUBLIC_API_BASE_URL` | ✅ Configured |
