# Deployment Plan — SignalStack

## Purpose

This document details everything required to deploy SignalStack to the cloud, split into two distinct phases:

- **Phase 1 — Solo Testing & Evaluation:** Single user (who is also the admin). Bare minimum infrastructure. No compliance overhead, no DR, no extra security measures. Just get the system running for one person to test.
- **Phase 2 — Production Rollout:** Multi-user, compliance-ready (SEBI, REQ-LEGAL-009), geo-redundant, fully observable, secured with WAF, penetration tested, and backed up per BCP requirements.

**Phase 1 tasks are marked with [P1]. Phase 2 tasks are marked with [P2].**

The code is not adjusted between phases — only the infrastructure and configuration change.

---

## Table of Contents

1. [Solution Overview](#1-solution-overview)
2. [Provisioning Model](#2-provisioning-model)
3. [Phase 1 — Solo Testing & Evaluation](#3-phase-1--solo-testing--evaluation)
4. [Phase 2 — Production Rollout](#4-phase-2--production-rollout)
5. [Free-Tier and Cost-Optimised Hosting Options](#5-free-tier-and-cost-optimised-hosting-options)
6. [Third-Party Integration Setup](#6-third-party-integration-setup)
7. [Secret and Configuration Management](#7-secret-and-configuration-management)
8. [CI/CD Pipeline Design](#8-cicd-pipeline-design)
9. [Environment Strategy](#9-environment-strategy)
10. [Disaster Recovery and Backup](#10-disaster-recovery-and-backup)
11. [Observability Setup](#11-observability-setup)
12. [Runbooks and Checklists](#12-runbooks-and-checklists)
13. [Appendix: Bicep Module Inventory](#13-appendix-bicep-module-inventory)

---

## 1. Solution Overview

### 1.1 Projects in the Solution

The repo (`SignalStack.sln`) contains 11 projects across 3 deployable application boundaries:

| Project | Path | Runtime | Stack | Deployable |
|---------|------|---------|-------|-----------|
| **SignalStack.Api** | `apps/api/` | .NET 10.0 | ASP.NET Core Web API | Yes |
| **SignalStack.Worker** | `apps/worker/` | .NET 10.0 | .NET Worker Service | Yes |
| **@signalstack/web** | `apps/web/` | Node.js 24 | Next.js 15 + TypeScript | Yes |
| **SignalStack.Seed** | `apps/seed/` | .NET 10.0 | Console app (sys_config seeder) | Migration-time only |
| **SignalStack.Configuration** | `packages/config/` | .NET 10.0 | Class library (shared config) | Library — not standalone |
| **SignalStack.MarketData** | `packages/market-data/` | .NET 10.0 | Class library (MDP abstraction) | Library — not standalone |
| **SignalStack.Migrations** | `packages/migrations/` | .NET 10.0 | Class library (MongoDB migrations) | Migration-time only |
| **SignalStack.SqlMigrations** | `packages/migrations/` | .NET 10.0 | Class library (PostgreSQL migrations) | Migration-time only |
| **SignalStack.Api.Tests** | `tests/api/` | .NET 10.0 | xUnit test project | CI only |
| **SignalStack.Worker.Tests** | `tests/worker/` | .NET 10.0 | xUnit test project | CI only |
| **SignalStack.Migrations.Tests** | `tests/migrations/` | .NET 10.0 | xUnit test project | CI only |

### 1.2 Application Dependencies

```
apps/web (Next.js 15)
  └── API calls → apps/api

apps/api (ASP.NET Core)
  ├── packages/config/SignalStack.Configuration
  └── packages/migrations/SignalStack.Migrations

apps/worker (.NET Worker Service)
  ├── apps/api  (shared domain models)
  ├── packages/config/SignalStack.Configuration
  ├── packages/market-data/SignalStack.MarketData
  ├── packages/migrations/SignalStack.Migrations
  └── packages/migrations/SignalStack.SqlMigrations
```

### 1.3 Data Store Dependencies

**Every app needs these Azure-hosted services to function (both phases):**

| Service | Used By | Purpose |
|---------|---------|---------|
| MongoDB | api, worker, seed | Operational data: users, signals, portfolio, notifications, sys_config |
| PostgreSQL | api, worker | Historical OHLCV data (marketdata + backtest databases) |
| Redis | worker, api | Coordination locks, singleton lease, cache, queue |
| Key Vault | api, worker | Secrets: FYERS credentials, OAuth secrets, Telegram token, signing keys |
| Azure App Configuration | api, worker | Shared non-secret technical configuration |

These are non-negotiable — the code references them directly. The difference between phases is the **tier/SKU** and **which optional services** are added.

---

## 2. Provisioning Model

### 2.1 Division of Responsibility (both phases)

| Layer | Who Provisions | Method |
|-------|---------------|--------|
| Cloud resources (App Services, DBs, Redis, KV, etc.) | You (manually) | Azure Portal / Azure CLI / Bicep (run locally) |
| GitHub Actions workflows | You + CI/CD setup | One-time commit of `.github/workflows/*.yml` |
| Application deployment | CI/CD pipelines | GitHub Actions after every push to main |
| Database migrations + seeding | CI/CD pipelines | Part of the deployment workflow |
| Third-party app registrations | You (manually) | OAuth console, FYERS dashboard, Telegram BotFather |

**The CI/CD pipeline never calls `az deployment group create` or any equivalent. It only deploys built application artefacts to already-provisioned Azure resources.**

---

## 3. Phase 1 — Solo Testing & Evaluation

### 3.1 What Phase 1 Is

A single-user deployment where you are the only user and also the admin. The goal is running the system to test signals, charts, portfolio tracking, and execution — **without any of the cost and complexity of a multi-user compliant deployment**.

### 3.2 What We Strip Out for Phase 1

| Category | Phase 2 (Production) | Phase 1 (Solo) |
|----------|---------------------|----------------|
| Environments | dev + staging + production | **Single environment** |
| Front Door / WAF | Premium Azure Front Door | **Not used** — direct App Service URL |
| Geo-redundancy | PostgreSQL DR in South India | **Not used** — single region |
| Deployment slots | staging → production swap | **Not used** — deploy directly |
| Recovery Services Vault | Full backup policies | **Not used** — built-in automated backups only |
| Application Insights | Full workspace-based | **Free tier or skip** |
| Log Analytics | Dedicated workspace | **Not needed** |
| Multi-region DR | Full DR plan with restore drills | **Not applicable** |
| Penetration testing | Required before Phase C | **Not required** |
| Formal runbooks | Full catalog required | **Not required** |
| Compliance docs | SEBI, ToS, Privacy Policy, ROPA | **Not required** |
| OAuth providers | Google + Microsoft + Facebook | **One provider (Google)** |
| Security scanning | SAST, dependency scan, secret scan | **Basic CI only** |
| Email notification channel | Azure Communication Services | **Not needed** — Telegram only |
| Backup retention policies | Configurable LTR | **Default automated backups** |
| Admin MFA enforcement | Required (REQ-BCP-009) | **Not enforced** |

### 3.3 What Stays (Code-Required, Both Phases)

These are not optional — the application code depends on them:

| Service | Phase 1 SKU | Estimated Monthly Cost |
|---------|-------------|----------------------|
| Key Vault | Standard | ~$0 |
| App Configuration | Free tier | $0 |
| MongoDB Atlas | M0 (free) | $0 |
| PostgreSQL (single DB) | Azure Database for PostgreSQL Flexible Server — Serverless (General Purpose, 1 vCore) | ~$5-15 (pauses when idle) |
| Redis Cache | Standard C0 (250 MB) | ~$15 |
| App Service Plan (Linux B1) | Shared plan for API + Worker + Web | ~$13 |
| Google OAuth | Free | $0 |
| FYERS API App | Free (evaluation) | $0 |
| Telegram Bot | Free | $0 |
| GitHub Actions | Included with GitHub plan | $0 |
| **Total estimated** | | **~$28-33/month** |

### 3.4 Phase 1 — Cloud Resource Checklist [P1]

Provision these **once**, manually (not through pipeline):

- [ ] **Resource group:** `rg-signalstack-solo` in Central India
- [ ] **Key Vault:** Standard SKU, `kv-signalstack-solo` in Central India (run `infra/azure/keyvault.bicep` or create via portal)
- [ ] **App Configuration:** Free tier, `appcs-signalstack-solo` in Central India
- [ ] **App Service Plan:** Linux B1, `asp-signalstack-solo` in Central India (shared by all 3 apps)
- [ ] **API App Service:** Linux .NET 10, `app-signalstack-api-solo` on the shared plan
- [ ] **Worker App Service:** Linux .NET 10, `app-signalstack-worker-solo` on the shared plan (run `infra/azure/worker-appservice.bicep` or create via portal — must stay at 1 instance)
- [ ] **Web App Service:** Linux Node.js 24, `app-signalstack-web-solo` on the shared plan
- [ ] **PostgreSQL:** Azure Database for PostgreSQL Flexible Server (Burstable, 1 vCore), `psql-signalstack-solo` in Central India — single database covering both marketdata and backtest schemas (use `sqlserver-backup.bicep` but skip geo-redundancy parameters)
- [ ] **Redis Cache:** Standard C0 (250 MB), `redis-signalstack-solo` in Central India
- [ ] **MongoDB Atlas:** M0 free tier cluster in Azure Central India region

**Total services created:** ~11 (compared to ~18 for Phase 2)

### 3.5 Phase 1 — App Service Configuration

Since all three apps share a single B1 plan, the configuration is:

| App | Runtime | Plan | URL |
|-----|---------|------|-----|
| API | .NET 10 (Linux) | asp-signalstack-solo | `https://app-signalstack-api-solo.azurewebsites.net` |
| Worker | .NET 10 (Linux) | asp-signalstack-solo | `https://app-signalstack-worker-solo.azurewebsites.net` |
| Web | Node.js 24 (Linux) | asp-signalstack-solo | `https://app-signalstack-web-solo.azurewebsites.net` |

**App Settings (App Service configuration blade) — all three apps:**

```
KEY_VAULT_ENDPOINT       →  https://kv-signalstack-solo.vault.azure.net/
APP_CONFIG_ENDPOINT      →  https://appcs-signalstack-solo.azconfig.io
SEED_ADMIN_EMAIL         →  your-email@example.com
ENVIRONMENT              →  Development
ASPNETCORE_ENVIRONMENT   →  Development
```

**Managed Identity:** Enable system-assigned managed identity on each App Service. Grant each identity `Key Vault Secrets User` role on the Key Vault (in Phase 1, all three apps get reader access; the API also needs `Secrets Officer` to write Telegram tokens and signed-payload keys).

### 3.6 Phase 1 — Key Vault Secrets

Populate these in Key Vault:

| Secret Name | Value |
|-------------|-------|
| `fyers--app-id` | FYERS APP ID from developer dashboard |
| `fyers--secret-key` | FYERS APP SECRET from developer dashboard |
| `oauth--google--client-id` | Google OAuth Client ID |
| `oauth--google--client-secret` | Google OAuth Client Secret |
| `telegram--bot-token` | Token from BotFather |
| `mongodb--connection-string` | MongoDB Atlas connection string (from M0 cluster) |
| `sqlserver--connection-string` | PostgreSQL connection string |
| `redis--connection-string` | Redis connection string |
| `session-signing-key` | Generate a new RSA 2048 key or use a random 256-bit key for HMAC |
| `orders-intent-hmac-key` | Random 256-bit key (needed once Phase 7 code runs) |

### 3.7 Phase 1 — CI/CD Pipelines

A minimal set. No separate security scanning, no slot swaps.

| Workflow | Purpose |
|----------|---------|
| `ci.yml` | Build + test + lint on every PR/push |
| `deploy-all.yml` | Build and deploy all 3 apps + run migrations on push to main |

The `deploy-all.yml` workflow is described in [Section 8.1](#81-phase-1--deploy-all-workflow).

---

## 4. Phase 2 — Production Rollout

### 4.1 When to Move to Phase 2

Phase 2 is entered when any of these conditions is met:
- A second user needs access to the system
- The system will handle real user data beyond your own
- Compliance requirements (SEBI, data residency, legal) become active
- You need uptime guarantees or DR capability

### 4.2 What Gets Added in Phase 2

| Addition | Purpose | Estimated Monthly Cost |
|----------|---------|----------------------|
| Separate dev/staging/prod environments | Safe deployment pipeline | ~$50-80 (additional minimal environments) |
| Front Door Premium + WAF | CDN, DDoS protection, OWASP CRS, rate limiting | ~$30-50 |
| Full PostgreSQL GP (2 vCore) | Two separate databases (marketdata + backtest) | ~$150-300 |
| PostgreSQL secondary (South India) | Geo-redundant DR standby | ~$70-150 |
| MongoDB Atlas M10+ | PITR, replication, production-grade IOPS | ~$50-100 |
| Redis Standard C1 (1 GB) | More capacity for queue/cache workloads | ~$50 |
| Application Insights | Full observability with alerts | ~$20-50/month depending on ingestion |
| Log Analytics Workspace | Centralised logging | ~$2-5/month per GB ingested |
| Recovery Services Vault | Full backup policy management | ~$5 |
| Deployment slots (staging) | Zero-downtime deployments | Included in App Service |
| App Service Plan P1v3 | Production-grade performance | ~$70-90 per plan |
| Separate API/Worker/Web plans | No resource contention | ~$140-270 (3 plans) |
| Azure Communication Services | Email fallback for admin alerts | ~$0 (pay per message) |
| Penetration testing (external) | Required before Phase C | ~$5,000-15,000 (one-time) |
| Total estimated | | **~$500-1,100+/month** |

### 4.3 Phase 2 — Full Resource Checklist [P2]

Each production environment needs:

- [ ] **Resource group:** `rg-signalstack-prd` in Central India
- [ ] **Key Vault:** Standard SKU, `kv-signalstack-prd` in Central India
- [ ] **App Configuration:** Standard tier (if Free tier limits exceeded)
- [ ] **API App Service Plan:** Linux P1v3, `asp-signalstack-api-prd` (may scale horizontally)
- [ ] **Worker App Service Plan:** Linux P1v3, `asp-signalstack-worker-prd` — **must stay at 1 instance**
- [ ] **Web App Service Plan:** Linux P1v3, `asp-signalstack-web-prd`
- [ ] **API App Service:** With staging slot, `app-signalstack-api-prd`
- [ ] **Worker App Service:** Preflight-checked singleton, `app-signalstack-worker-prd`
- [ ] **Web App Service:** With staging slot, `app-signalstack-web-prd`
- [ ] **PostgreSQL:** `psql-signalstack-prd` in Central India with geo-replication to South India
- [ ] **Market Data DB:** Flexible Server General Purpose (2 vCore), `psqldb-signalstack-marketdata-prd`
- [ ] **Backtest DB:** Flexible Server General Purpose (2 vCore), `psqldb-signalstack-backtest-prd`
- [ ] **PostgreSQL Secondary:** In South India for DR
- [ ] **Redis:** Standard C1 (1 GB), `redis-signalstack-prd` with separate DB indexes (0 = cache, 1 = locks)
- [ ] **MongoDB Atlas:** M10 or higher for PITR and replication
- [ ] **Front Door Premium:** WAF with OWASP CRS + Bot Manager
- [ ] **Recovery Services Vault:** Backup policies for PostgreSQL
- [ ] **Application Insights:** Workspace-based
- [ ] **Log Analytics Workspace:** Linked to App Insights

---

## 5. Free-Tier and Cost-Optimised Hosting Options

### 5.1 Azure Free Tier Services

These work for Phase 1 without the B1 plan cost:

| Service | Free Offer | Phase 1 Viable? |
|---------|-----------|-----------------|
| **App Service F1 (Linux)** | 60 CPU minutes/day, 1 GB storage | Only for very intermittent testing — **app goes to sleep** after 20 min idle, no custom domain, shared infra |
| **App Configuration** | Free tier — 1 store | **Yes** |
| **Key Vault Standard** | 1M transactions/month | **Yes** |
| **Azure Database for PostgreSQL Flexible Server** | 100,000 vCore seconds/month, 32 GB, 1 DB | **Yes** — single DB covers marketdata + backtest schemas. Pauses after 1 hour idle |
| **Azure Cosmos DB (MongoDB API)** | 1000 RU/s, 25 GB | Partial — RU-based pricing works differently, some aggregation pipelines may not work identically to MongoDB. Test first. |
| **Application Insights** | 5 GB/month ingested | **Yes** for basic monitoring |
| **GitHub Actions** | 2,000-3,000 minutes/month | **Yes** |

**App Service F1 sleep problem:** The F1 tier spins down after 20 minutes of no HTTP traffic. For a trading platform that needs the Worker running during market hours, this is a dealbreaker — the Worker would stop processing market data. **B1 at ~$13/month is the recommended minimum for Phase 1** because it provides always-on capability.

### 5.2 Non-Azure Providers

These reduce Phase 1 cost further but require different infrastructure management.

| Provider | Free Tier | India Region | .NET 10 + SQL + MongoDB + Redis? | Estimated Phase 1 Cost |
|----------|-----------|-------------|----------------------------------|----------------------|
| **Oracle Cloud (Always Free)** | 4 ARM cores + 24 GB RAM VM, 200 GB storage, 10 TB egress | Mumbai | Run Docker Compose on the VM with all services (PostgreSQL via Docker, MongoDB, Redis). .NET runs on Linux. | **$0/month** |
| **AWS Free Tier** (12-month) | t2.micro (750h/mo), 20 GB RDS, 5 GB S3 | Mumbai (ap-south-1) | t2.micro is too small for all services. Would need multiple instances. | ~$10-20/month after free tier |
| **Google Cloud Free** | e2-micro VM (1/month), 2M Cloud Functions | Mumbai (asia-south1) | e2-micro too small for full stack. Cloud Run for stateless apps. | ~$15-25/month |

**Oracle Cloud ARM VM ($0/month) — the most cost-effective Phase 1 option:**

Run everything on a single always-free VM using Docker Compose (matching `infra/docker/docker-compose.yml`):

```
Single Oracle Cloud VM (Ampere A1, 4 OCPU, 24 GB RAM)
  ├── Docker: MongoDB 7
  ├── Docker: PostgreSQL 16
  ├── Docker: Redis 7
  ├── Docker: OTLP/HTTP export (no collector sidecar)
  ├── Process 1: dotnet SignalStack.Api.dll
  ├── Process 2: dotnet SignalStack.Worker.dll
  └── Process 3: npm start (apps/web)
```

**Trade-offs:**
- You manage the VM (OS updates, Docker upgrades, monitoring uptime)
- No managed database backups (set up your own cron-based DB dumps)
- Single point of failure (one VM goes down, everything goes down)
- REQ-LEGAL-009 data residency concern if using the India region — **acceptable for Phase 1 solo testing** where you are the only user and any real data belongs to you
- Requires a public IP and setting up a reverse proxy (Nginx/Caddy) for HTTPS with Let's Encrypt

### 5.3 Phase 1 Cost Tiers Summary

| Approach | Monthly Cost | Effort | Reliability |
|----------|-------------|--------|-------------|
| **All Azure managed (B1 + free tiers)** | ~$28-33 | Low | High — managed services |
| **Oracle Cloud VM + Docker** | $0 | Medium | Low — self-managed, single VM |
| **Hybrid: Azure essentials + local dev** | ~$0-15 | Medium | Medium — mix of local and cloud |

**The B1 plan is recommended for Phase 1** unless cost is a hard constraint. At ~$28-33/month it gives you managed services, proper backups, and the ability to focus on testing features rather than babysitting a VM.

---

## 6. Third-Party Integration Setup

### 6.1 FYERS API Application [P1]

**What you need:**
- FYERS developer account (register at `https://developer.fyers.in/`)
- A registered FYERS API application (app_id + secret_key)
- Redirect URI configured for your deployed portal domain

**Setup steps:**

1. Register/Login at the FYERS Developer Dashboard
2. Create a new API application:
   - App name: `SignalStack` (or your choice)
   - **Phase 1 redirect:** `https://app-signalstack-api-solo.azurewebsites.net/api/v1/auth/fyers/callback`
   - **Phase 2 redirect:** `https://{your-domain}/api/v1/auth/fyers/callback`
   - (Local dev) `http://localhost:3000/api/v1/auth/fyers/callback`
3. Note your **APP ID** and **SECRET KEY**
4. Store both in Azure Key Vault:
   - `fyers--app-id`
   - `fyers--secret-key`
5. FYERS token exchange happens per-user at runtime:
   - **You (admin)**: generate daily token through the portal
   - Phase 2+: per-user tokens for each user's account data
6. **Rate limits** are stored in `sys_config` under `integrations.fyers.rate_limit.*`
7. FYERS API Connect JS widget URL: `https://api-connect-docs.fyers.in/fyers-lib.js`

**Important FYERS contract items (from docs/fyers_api_integration_guide.md):**
- REST API base URL: `https://api-t1.fyers.in/v3`
- Data WebSocket URL: `wss://user.fyers.in/socket/...` (browser tier only)
- Order placement REST API: **must not be called server-side** (non-negotiable guardrail)

### 6.2 Google OAuth 2.0 [P1]

**What you need:**
- Google Cloud Console project
- OAuth 2.0 Client ID + Client Secret
- Authorised redirect URIs

**Setup steps:**

1. Go to `https://console.cloud.google.com/`
2. Create a new project (or use existing)
3. Navigate to **APIs & Services → Credentials**
4. Create **OAuth 2.0 Client IDs** → **Web Application**
5. Configure authorised JavaScript origins:
   - **Phase 1:** `https://app-signalstack-web-solo.azurewebsites.net`
   - **Phase 2:** `https://{your-domain}`
   - Local dev: `http://localhost:3000`
6. Configure authorised redirect URIs:
   - **Phase 1:** `https://app-signalstack-api-solo.azurewebsites.net/api/v1/auth/google/callback`
   - **Phase 2:** `https://{your-domain}/api/v1/auth/google/callback`
   - Local dev: `http://localhost:3000/api/v1/auth/google/callback`
7. Note the **Client ID** and **Client Secret**
8. Store in Azure Key Vault:
   - `oauth--google--client-id`
   - `oauth--google--client-secret`
9. Reference in App Service app settings (via Key Vault reference):
   ```
   Authentication:Google:ClientId → @Microsoft.KeyVault(SecretUri=https://kv-signalstack-solo.vault.azure.net/secrets/oauth--google--client-id)
   Authentication:Google:ClientSecret → @Microsoft.KeyVault(SecretUri=https://kv-signalstack-solo.vault.azure.net/secrets/oauth--google--client-secret)
   ```

### 6.3 Microsoft OAuth 2.0 (Entra ID) [P2 only]

**Phase 1:** Skip this. One OAuth provider (Google) is sufficient for solo testing.

**Phase 2 setup:**

1. Go to **Microsoft Entra ID → App registrations → New registration**
2. Name: `SignalStack Portal`
3. Redirect URI: Web → `https://{your-domain}/api/v1/auth/microsoft/callback`
4. Note the **Application (client) ID** and **Directory (tenant) ID**
5. Create a client secret
6. Store in Azure Key Vault:
   - `oauth--microsoft--client-id`
   - `oauth--microsoft--tenant-id`
   - `oauth--microsoft--client-secret`

### 6.4 Facebook/Meta OAuth [P2 only]

**Phase 1:** Skip this. Google is sufficient for solo testing.

**Phase 2 setup:**

1. Go to `https://developers.facebook.com/`
2. Create a new app → add **Facebook Login** product
3. Configure OAuth redirect URIs
4. Store App ID and App Secret in Key Vault

**Note:** Per REQ-BCP-009, Facebook/Meta OAuth is **blocked for admin sign-in** (no MFA claim in standard OAuth response).

### 6.5 Telegram Bot [P1]

**What you need:**
- Telegram Bot Token from BotFather

**Setup steps:**

1. Open Telegram and search for `@BotFather`
2. Send `/newbot` and follow prompts:
   - Name: `SignalStack Bot` (or your choice)
   - Username: `signalstack_bot` (or similar)
3. Note the Bot Token (format: `1234567890:ABCdefGHIjklmNOPqrstUVwxyz`)
4. Store in Azure Key Vault:
   - `telegram--bot-token`

### 6.6 TradingView Widgets [P1]

**No setup required.** TradingView fundamental widgets (Financials, Fundamental Data, Company Profile) are loaded as iframe embeds on the chart page using `NSE:{symbol}` format directly from TradingView's servers. No API key needed.

---

## 7. Secret and Configuration Management

### 7.1 Configuration Tiers (per REQ-CONFIG) — Both Phases

| Tier | Source | Examples | Managed By |
|------|--------|---------|-----------|
| **Tier 1: Bootstrap** | Environment variables + Key Vault references | DB connection strings, API keys, admin email | DevOps at deploy time |
| **Tier 2: Shared technical** | Azure App Configuration | OTLP endpoint, feature flags, log levels | Through portal |
| **Tier 3: Runtime** | MongoDB `sys_config` | Job intervals, risk thresholds, rate limits | Through portal |

### 7.2 Azure Key Vault Required Secrets

| Secret Name | Description | Required By | Phase |
|-------------|-------------|-------------|-------|
| `fyers--app-id` | FYERS API application ID | API, Worker | P1 |
| `fyers--secret-key` | FYERS API application secret key | API, Worker | P1 |
| `oauth--google--client-id` | Google OAuth client ID | API | P1 |
| `oauth--google--client-secret` | Google OAuth client secret | API | P1 |
| `oauth--microsoft--client-id` | Microsoft OAuth client ID | API | P2 |
| `oauth--microsoft--tenant-id` | Microsoft Entra tenant ID | API | P2 |
| `oauth--microsoft--client-secret` | Microsoft OAuth client secret | API | P2 |
| `oauth--facebook--app-id` | Facebook/Meta App ID | API | P2 |
| `oauth--facebook--app-secret` | Facebook/Meta App secret | API | P2 |
| `telegram--bot-token` | Telegram bot token | Worker | P1 |
| `mongodb--connection-string` | MongoDB connection string | API, Worker, Seed | P1 |
| `sqlserver--connection-string` | PostgreSQL connection string | API, Worker | P1 |
| `redis--connection-string` | Redis connection string (to Standard C0 in P1, C1 in P2) | API, Worker | P1 |
| `orders-intent-hmac-key` | HMAC key for signed order payloads | API | P1 (Phase 7 code) |
| `session-signing-key` | Session JWT signing key | API | P1 |

Naming convention: `{domain}--{key}`

### 7.3 Azure App Configuration Keys [P1]

Non-secret shared technical configuration:

| Key | Phase 1 Example Value | Phase 2 Example Value |
|-----|----------------------|----------------------|
| `Telemetry:Otlp:Endpoint` | *(leave blank)* | OTLP endpoint (e.g. `https://otlp.eu01.nr-data.net:443`) |
| `Telemetry:Otlp:ApiKey` | *(leave blank)* | Provider API key |
| `Logging:LogLevel:Default` | `Information` | `Information` |
| `Logging:LogLevel:SignalStack` | `Debug` | `Information` |

### 7.4 Environment Variables (Tier 1 — Bootstrap)

These are required at application startup before App Configuration and Key Vault are loaded.

| Variable | Phase 1 Example | Source |
|----------|----------------|--------|
| `KEY_VAULT_ENDPOINT` | `https://kv-signalstack-solo.vault.azure.net/` | App Service setting |
| `APP_CONFIG_ENDPOINT` | `https://appcs-signalstack-solo.azconfig.io` | App Service setting |
| `SEED_ADMIN_EMAIL` | Your email | Deploy pipeline param |
| `ENVIRONMENT` | `Development` | App Service setting |
| `ASPNETCORE_ENVIRONMENT` | `Development` | App Service setting |

### 7.5 MongoDB sys_config Required Seed

The `apps/seed/SignalStack.Seed` console application seeds `sys_config` on first deploy. The full seed table is defined in `docs/system-config.md`.

**Phase 1 note:** The legal-disclaimer related sys_config keys (`legal.disclaimer.short_text`, `legal.disclaimer.long_url`) are deployment-specific and must be seeded with meaningful values even in Phase 1 — the code expects them. Set them to reasonable placeholder values during the seed step.

---

## 8. CI/CD Pipeline Design

### 8.1 Phase 1 — Deploy-All Workflow

A single workflow that builds and deploys everything. No slot swaps, no separate security scanning runners.

```yaml
# .github/workflows/deploy-all.yml
name: Build and Deploy (Phase 1)
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        include:
          - name: dotnet
            working-directory: .
            build: dotnet build SignalStack.sln -c Release
            test: dotnet test SignalStack.sln -c Release --no-build
          - name: web
            working-directory: apps/web
            build: npm run build
            test: npm test -- --run
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        if: matrix.name == 'dotnet'
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Setup Node
        if: matrix.name == 'web'
        uses: actions/setup-node@v4
        with:
          node-version: '24'
          cache: 'npm'
          cache-dependency-path: apps/web/package-lock.json
      - name: Install dependencies
        if: matrix.name == 'web'
        run: npm ci
        working-directory: ${{ matrix.working-directory }}
      - name: Build
        run: ${{ matrix.build }}
        working-directory: ${{ matrix.working-directory }}
      - name: Test
        run: ${{ matrix.test }}
        working-directory: ${{ matrix.working-directory }}

  deploy:
    needs: build-and-test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      # Step 1: Run database migrations and seeding
      - name: PostgreSQL migrations
        run: dotnet run --project packages/migrations/SignalStack.SqlMigrations/SignalStack.SqlMigrations.csproj
        env:
          ConnectionStrings__SqlServer: ${{ secrets.SQL_CONNECTION_STRING }}

      - name: MongoDB migrations
        run: dotnet run --project packages/migrations/SignalStack.Migrations/SignalStack.Migrations.csproj
        env:
          ConnectionStrings__MongoDb: ${{ secrets.MONGO_CONNECTION_STRING }}

      - name: sys_config seeding
        run: dotnet run --project apps/seed/SignalStack.Seed.csproj
        env:
          ConnectionStrings__MongoDb: ${{ secrets.MONGO_CONNECTION_STRING }}
          SEED_ADMIN_EMAIL: ${{ vars.SEED_ADMIN_EMAIL }}
          SEED_DISCLAIMER_SHORT: ${{ vars.SEED_DISCLAIMER_SHORT }}
          SEED_DISCLAIMER_URL: ${{ vars.SEED_DISCLAIMER_URL }}

      # Step 2: Publish and deploy API
      - run: dotnet publish apps/api/SignalStack.Api.csproj -c Release -o publish/api
      - name: Deploy API
        uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ vars.API_APP_NAME }}
          package: publish/api
          publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE_API }}
      - name: Warm API
        run: curl -sS --fail -o /dev/null "https://${{ vars.API_APP_NAME }}.azurewebsites.net/health"

      # Step 3: Publish and deploy Worker
      - run: dotnet publish apps/worker/SignalStack.Worker.csproj -c Release -o publish/worker
      - name: Deploy Worker
        uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ vars.WORKER_APP_NAME }}
          package: publish/worker
          publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE_WORKER }}

      # Step 4: Build and deploy Web
      - uses: actions/setup-node@v4
        with:
          node-version: '24'
      - run: npm ci
        working-directory: apps/web
      - run: npm run build
        working-directory: apps/web
      - name: Deploy Web
        uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ vars.WEB_APP_NAME }}
          package: apps/web
          publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE_WEB }}
```

**GitHub Actions secrets and variables for Phase 1:**

| Type | Name | Value |
|------|------|-------|
| Secret | `AZURE_WEBAPP_PUBLISH_PROFILE_API` | Publish profile from API App Service |
| Secret | `AZURE_WEBAPP_PUBLISH_PROFILE_WORKER` | Publish profile from Worker App Service |
| Secret | `AZURE_WEBAPP_PUBLISH_PROFILE_WEB` | Publish profile from Web App Service |
| Secret | `SQL_CONNECTION_STRING` | PostgreSQL connection string |
| Secret | `MONGO_CONNECTION_STRING` | MongoDB Atlas connection string |
| Variable | `API_APP_NAME` | `app-signalstack-api-solo` |
| Variable | `WORKER_APP_NAME` | `app-signalstack-worker-solo` |
| Variable | `WEB_APP_NAME` | `app-signalstack-web-solo` |
| Variable | `SEED_ADMIN_EMAIL` | your-email@example.com |
| Variable | `SEED_DISCLAIMER_SHORT` | "Trading involves risk. Not investment advice." |
| Variable | `SEED_DISCLAIMER_URL` | URL to long-form disclaimer |

### 8.2 Phase 2 — Full CI/CD Pipeline

The Phase 2 pipeline adds:

| Workflow | What's Added vs Phase 1 |
|----------|------------------------|
| `ci.yml` | Separate security scanning job (dependency vuln scan, SAST, TruffleHog secret scan) |
| `deploy-api.yml` | Staging slot deploy → warmup → slot swap to production. Path-filtered (only on api/ or packages/ changes) |
| `deploy-worker.yml` | Preflight singleton check before deploy (`Test-WorkerSingletonPreflight.ps1`) |
| `deploy-web.yml` | Separate Node.js build pipeline. Path-filtered to web/ changes only |
| `deploy-migrations.yml` | Separate migration workflow, runs before API/Worker deploys. Includes REQ-MIGRATION-007 snapshot verification |
| `security-scan-scheduled.yml` | Weekly scheduled CVE scan on main branch |
| `cleanup-preview.yml` | PR-based preview environment cleanup |

See [the prior version of this document for the full Phase 2 workflow YAML examples].

### 8.3 Pipeline Ordering

**Phase 1:**
```
Push to main → CI (build + test) → Deploy-all (migrations → API → Worker → Web)
```

**Phase 2:**
```
Push to main → CI (build + test + security scan)
  ├─→ Migrations (PostgreSQL → Mongo → Seed)
  ├─→ API (staging → warm → swap)
  ├─→ Worker (preflight → deploy)
  └─→ Web (build → deploy)
```

---

## 9. Environment Strategy

### 9.1 Phase 1 — Single Environment

| Environment | Name | Deployed By | Data |
|-------------|------|-------------|------|
| **Solo** | `rg-signalstack-solo` | CI/CD from main | Your real FYERS account data |

No staging, no slots. Deploy directly to the production (only) App Service. If a deploy breaks, redeploy the previous commit.

### 9.2 Phase 2 — Three-Environment Strategy

| Environment | Purpose | Provisioned | Deployed By | Data |
|-------------|---------|-------------|-------------|------|
| **Development (dev)** | Active development, testing features | Minimal (free-tier friendly) | CI from feature branches | Synthetic test data |
| **Staging (stg)** | Pre-production validation | Full stack, smaller SKUs | CI from main branch → staging slot | Anonymised/partial production data |
| **Production (prd)** | Live user-facing platform | Full stack, production SKUs | CI from main → staging slot → swap | Real user data |

### 9.3 Phase 1 Naming Convention

```
Resource group:            rg-signalstack-solo
API App Service:           app-signalstack-api-solo
Worker App Service:        app-signalstack-worker-solo
Web App Service:           app-signalstack-web-solo
Shared Plan:               asp-signalstack-solo
PostgreSQL:                psql-signalstack-solo
PostgreSQL Database:       psqldb-signalstack-solo       (single DB, both schemas)
Redis:                     redis-signalstack-solo
Key Vault:                 kv-signalstack-solo
App Configuration:         appcs-signalstack-solo
```

### 9.4 Phase 2 Naming Convention

```
Resource group:            rg-signalstack-{env}
API App Service:           app-signalstack-api-{env}
Worker App Service:        app-signalstack-worker-{env}
Web App Service:           app-signalstack-web-{env}
API Plan:                  asp-signalstack-api-{env}
Worker Plan:               asp-signalstack-worker-{env}
Web Plan:                  asp-signalstack-web-{env}
PostgreSQL:                psql-signalstack-{env}
Market Data DB:            psqldb-signalstack-marketdata-{env}
Backtest DB:               psqldb-signalstack-backtest-{env}
Redis:                     redis-signalstack-{env}
Key Vault:                 kv-signalstack-{env}
App Configuration:         appcs-signalstack-{env}
Application Insights:      appi-signalstack-{env}
Front Door:                fd-signalstack-{env}
Recovery Vault:            rsv-signalstack-{env}
```

---

## 10. Disaster Recovery and Backup

### 10.1 Phase 1 — Minimal Approach

| Data Store | Backup Method | What You Lose | Acceptable for Solo? |
|-----------|-------------|---------------|---------------------|
| MongoDB (Atlas M0) | Atlas automated snapshots (shared cluster) | No PITR, snapshots are best-effort | **Yes** — you can re-seed most data from FYERS |
| PostgreSQL | Azure automated backups (7-day PITR included) | Can restore to any point in last 7 days | **Yes** — built-in, no extra config |
| Key Vault | Soft-delete enabled (90-day window) | Purge-protected by default | **Yes** |
| Redis | Not backed up | Cache/locks are ephemeral | **Yes** — locks are re-acquired, cache rebuilt |

**Phase 1 backup summary:** Rely on default automated backups that come with the services. No additional backup infrastructure is needed. If you lose everything, you can re-deploy from CI/CD and re-seed FYERS data. The cost of managing formal backups exceeds the value of the data during solo testing.

### 10.2 Phase 2 — Full BCP Compliance

| Data Store | Backup Type | Frequency | Retention | RPO | RTO |
|-----------|-------------|-----------|-----------|-----|-----|
| MongoDB (Atlas M10+) | Continuous PITR | Real-time | ≥24 hours | 1 hour | 2 hours |
| PostgreSQL marketdata | Full + Write-ahead log archiving | Daily full, WAL archive continuous | Daily: 5 days, Weekly: 4 weeks, Monthly: 3 months | 24 hours | 4 hours |
| PostgreSQL backtest | Weekly full | Weekly (Sunday) | 8 weeks | 7 days | 8 hours |
| Key Vault | Soft-delete + purge protection | Platform-managed | 90 days | — | — |
| App Configuration | Azure-managed snapshots | Automatic | 7-day PITR | — | — |

All Phase 2 backups geo-redundant: Central India → South India per REQ-BCP-005 / REQ-LEGAL-009.

### 10.3 Phase 2 — DR Scenarios

| Scenario | Response |
|----------|----------|
| API service down | Swap staging slot, or redeploy previous version |
| Worker service down | App Service auto-restart; verify singleton lease after restart |
| PostgreSQL region failure | Manual failover to South India secondary |
| MongoDB Atlas outage | Restore from PITR to new cluster; update connection string in Key Vault |
| Full Azure region failure | Promote South India PostgreSQL; deploy App Services from Bicep to South India; update DNS/Front Door; restore MongoDB from Atlas cross-region snapshot |

---

## 11. Observability Setup

### 11.1 Phase 1 — Minimal Observability

**Just enough to debug issues:**

- App Service **Diagnostic logs** → File System (enable on each App Service)
- App Service **Log stream** → View live logs in portal during development
- **Serilog console output** → Already wired in code; visible in Log stream and `docker logs`
- Application Insights optional → Can add free tier later without code change (just update App Configuration OTLP endpoint)

That's it. No dedicated OTLP collector, no Application Insights, no alerts. For solo testing you can read logs from the App Service Log Stream or the Azure CLI:

```
az webapp log tail --name app-signalstack-api-solo --resource-group rg-signalstack-solo
```

The code already emits OTLP. When you're ready for observability, just set `Telemetry:Otlp:Endpoint` and `Telemetry:Otlp:ApiKey` in App Configuration and telemetry flows automatically.

### 11.2 Phase 2 — Full Observability

```
┌──────────┐     ┌──────────────┐
│  API     │────▶│ Application  │
│  Worker  │────▶│ Insights     │
│  Web     │────▶│ (or Grafana) │
└──────────┘     └──────────────┘
```

- Update `Telemetry:Otlp:Endpoint` and `Telemetry:Otlp:ApiKey` in App Configuration (no collector needed)
- Application Insights (Workspace-based) in Central India
- Log Analytics Workspace linked to App Insights
- Key alerts: API 5xx rate, Worker process down, singleton violation, PostgreSQL CPU > 80%, FYERS token expiry

---

## 12. Runbooks and Checklists

### 12.1 Phase 1 — First Deployment Checklist [P1]

Before the first deployment, complete these:

- [ ] Azure subscription active with Central India region access
- [ ] **Resource group** `rg-signalstack-solo` created
- [ ] **Key Vault** `kv-signalstack-solo` created and populated with all secrets (Section 7.2)
- [ ] **App Configuration** `appcs-signalstack-solo` created (free tier)
- [ ] **App Service Plan** `asp-signalstack-solo` (Linux B1) created
- [ ] **API App Service** `app-signalstack-api-solo` created (Linux .NET 10, on shared plan)
- [ ] **Worker App Service** `app-signalstack-worker-solo` created (Linux .NET 10, on shared plan)
- [ ] **Web App Service** `app-signalstack-web-solo` created (Linux Node.js 24, on shared plan)
- [ ] **PostgreSQL** `psql-signalstack-solo` created with single database
- [ ] **Redis Cache** `redis-signalstack-solo` (Standard C0) created
- [ ] **MongoDB Atlas** M0 cluster created in Azure Central India region
- [ ] **Managed identity** enabled on all 3 App Services with Key Vault access
- [ ] **App Service app settings** configured with `KEY_VAULT_ENDPOINT`, `APP_CONFIG_ENDPOINT`, `SEED_ADMIN_EMAIL`
- [ ] **CORS** configured on the API App Service to allow the Web app origin
- [ ] FYERS API application registered and redirect URI configured for the API app URL
- [ ] Google OAuth 2.0 credentials created with redirect URI for the API app URL
- [ ] Telegram bot created via BotFather
- [ ] GitHub repository created with `main` branch
- [ ] GitHub Actions secrets added (publish profiles + connection strings)
- [ ] GitHub Actions variables added (app names, admin email)
- [ ] Push to `main` triggers the deploy-all workflow

### 12.2 Phase 1 — Post-Deployment Verification [P1]

After every deployment:

- [ ] `/health` endpoint returns 200 for API, Worker, and Web
- [ ] API can connect to MongoDB and PostgreSQL
- [ ] Worker started successfully and acquired Redis singleton lease
- [ ] Google OAuth sign-in works (you can log in)
- [ ] You are auto-elevated to admin (your email matches `SEED_ADMIN_EMAIL`)
- [ ] Dashboard loads with empty portfolio state
- [ ] No unexpected errors in App Service Log stream

### 12.3 Phase 2 — Pre-Production Checklist [P2]

Before moving to Phase 2, complete these additional items:

- [ ] Separate dev/staging/prod resource groups created
- [ ] Front Door Premium + WAF deployed with OWASP CRS
- [ ] Custom domain configured and verified
- [ ] SSL certificates provisioned (Front Door managed TLS)
- [ ] Full PostgreSQL deployment with separate marketdata + backtest databases
- [ ] PostgreSQL geo-replication to South India configured
- [ ] MongoDB Atlas upgraded to M10+ with PITR enabled
- [ ] Redis upgraded to Standard C1 with separate DB indexes
- [ ] Application Insights + Log Analytics deployed
- [ ] Recovery Services Vault deployed with backup policies
- [ ] OTLP endpoint and API key configured for observability vendor
- [ ] Microsoft and Facebook OAuth providers registered
- [ ] Azure Communication Services set up for email fallback
- [ ] All three OAuth providers configured with production redirect URIs
- [ ] Staging deployment slots configured on API and Web App Services
- [ ] Full CI/CD pipeline deployed (separate deploy workflows per service)
- [ ] Security scanning workflows added (dependency, SAST, secret scan)
- [ ] Worker singleton preflight script configured in pipeline
- [ ] Admin MFA enforced (Google/Microsoft only)
- [ ] Penetration test scheduled (required before Phase C)

### 12.4 FYERS Admin Token Daily Procedure [P1+P2]

Each trading day, you must:

1. Login to the admin portal
2. Navigate to **FYERS Token Management**
3. Generate a new daily token before 08:30 IST (market open)
4. Verify the pre-market token check passes (automatic in later builds)

**Phase 2 automated guardrails:**
- Pre-market check at 08:30 IST
- Secondary check at 15:00 IST before DataSync
- Admin notified via Telegram + email if token is missing or expired

---

## 13. Appendix: Bicep Module Inventory

### 13.1 Existing Bicep Modules

| Module | File | Phase 1 Use? | Phase 2 Use? |
|--------|------|-------------|-------------|
| Worker App Service | `infra/azure/worker-appservice.bicep` | **Yes** — uses the singleton enforcement (`numberOfWorkers: 1`) | **Yes** |
| Key Vault | `infra/azure/keyvault.bicep` | **Yes** — standard SKU, soft-delete, RBAC. Purge protection and 90-day retention apply in both phases | **Yes** |
| PostgreSQL + Databases | `infra/azure/sqlserver-backup.bicep` | **Partially** — use for the PostgreSQL server itself, but skip geo-redundancy and secondary region for Phase 1 | **Yes** — full deployment |
| Front Door + WAF | `infra/azure/frontdoor-waf.bicep` | **No** — direct App Service URL is fine for solo testing | **Yes** |
| Backup Geo-Redundancy | `infra/azure/backup-geo-redundancy.bicep` | **No** — not needed for solo testing | **Yes** |

### 13.2 Missing Bicep Modules

| Module | Needed In | Recommended File |
|--------|-----------|-----------------|
| API App Service | P1, P2 | `infra/azure/api-appservice.bicep` |
| Web App Service | P1, P2 | `infra/azure/web-appservice.bicep` |
| Redis Cache | P1, P2 | `infra/azure/redis-cache.bicep` |
| App Configuration | P1, P2 | `infra/azure/app-configuration.bicep` |
| Application Insights | P2 | `infra/azure/application-insights.bicep` |
| Log Analytics Workspace | P2 | `infra/azure/log-analytics.bicep` |
| Main orchestration | optional | `infra/azure/main.bicep` |

---

## Document History

| Date | Author | Change |
|------|--------|--------|
| 2026-05-16 | Claude | Initial deployment plan created |
| 2026-05-16 | Claude | Restructured into Phase 1 (solo/testing) and Phase 2 (production) |

---
