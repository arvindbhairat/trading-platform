# Phase 1 Deployment — $0/Month Architecture (Supabase + Atlas + Railway)

> **Update (May 2026):** Replaced Oracle Cloud VM with managed free-tier services. API + Worker hosted on Railway (auto-HTTPS, no Caddy/domain needed). Databases fully managed (Supabase, MongoDB Atlas, Redis Cloud). OTel Collector removed — .NET SDK exports directly to New Relic.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Railway Project — signalstack                          │
│                                                         │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐  │
│  │ Web (Next.js)    │  │ API Service       │  │ Worker Service    │  │
│  │ *.railway.app    │  │ (.NET)            │  │ (.NET background) │  │
│  │ auto HTTPS       │  │ *.railway.app    │  │ (no public port)  │  │
│  │                  │  │ auto HTTPS        │  │ always-on         │  │
│  └────────┬─────────┘  └───────┬──────────┘  └────────┬─────────┘  │
│           │                    │                      │             │
└───────────┼────────────────────┼──────────────────────┼─────────────┘
            │ HTTPS calls        │                      │
            │                    ▼                      ▼
            │    ┌──────────────────────┐  ┌──────────────────────┐  ┌──────────────────────┐
            │    │  MongoDB Atlas M0    │  │  Supabase Free        │  │  Redis Cloud Free    │
            │    │  (Free Tier)         │  │  (PostgreSQL)         │  │  (redis.com)         │
            │    │  Nifty 500 data      │  │  OHLCV historical     │  │  Caching, sessions   │
            │    │  512MB storage       │  │  500MB database       │  │  50MB                │
            │    └──────────────────────┘  └──────────────────────┘  └──────────────────────┘
            │
            ▼
┌─────────────────────────────────────────────────────────┐
│  New Relic Free (100 GB/mo logs, 100 GB/mo traces,      │
│   10k metrics/month)                                     │
│  Direct OTLP export from .NET SDK — no collector         │
└─────────────────────────────────────────────────────────┘

Build and deployment of all three services is handled by Railway's
GitHub integration. Railway watches the repo and auto-deploys each
service on push to main. No GitHub Actions deploy workflow needed.
```

## Cost Breakdown

| Component | Service | Monthly Cost | Notes |
|-----------|---------|-------------|-------|
| **Web** (Next.js) | Railway | **included** | Third service in same Railway project |
| **API** (.NET) | Railway | **$5-10** | Docker service, always-on, auto-HTTPS |
| **Worker** (.NET) | Railway | **included** | Second service in same Railway project |
| **MongoDB** | MongoDB Atlas M0 | **$0** | 512 MB shared storage, free forever |
| **PostgreSQL** | Supabase Free | **$0** | 500 MB database, 2 GB bandwidth |
| **Redis** | Redis Cloud Free | **$0** | 50 MB, redis.com |
| **Observability** | New Relic Free | **$0** | Direct OTLP — URL + license key only |
| **CI/CD** | Railway (auto-deploy) + GitHub Actions (seeder only) | **$0** | Railway builds and deploys; Actions runs seeder on demand |
| **Domain** | Not needed | **$0** | Railway provides `*.railway.app` |
| **Total** | | **~$5-10/month** | |

---

## Step 1: Supabase (PostgreSQL)

### 1.1 Create a Supabase Project

1. Go to [https://supabase.com](https://supabase.com) and sign up (GitHub OAuth recommended)
2. Click **New project**
3. Fill in:

   | Setting | Value |
   |---------|-------|
   | **Name** | `signalstack` |
   | **Database Password** | Generate a strong password (save this) |
   | **Region** | Choose the closest to you (e.g., `Singapore` for India) |
   | **Pricing Plan** | **Free** |

4. Click **Create new project** (takes ~2 minutes to provision)

### 1.2 Get Your Connection String

1. In your Supabase project dashboard, go to **Project Settings → Database**
2. Under **Connection string**, find the **URI** entry:

   ```
   postgresql://postgres:<password>@db.<ref>.supabase.co:5432/postgres
   ```

3. Replace `<password>` with the database password
4. **Important:** Supabase enforces SSL/TLS. EF Core / Npgsql handles this automatically with `SSL Mode=Require` in the connection string.

### 1.3 Connection String for .NET Configuration

```
Host=db.<ref>.supabase.co;Port=5432;Database=signalstack;Username=postgres;Password=<password>;SSL Mode=Require;Trust Server Certificate=true;
```

### 1.4 Schema Management

Supabase is PostgreSQL (not SQL Server). The EF Core provider must be changed:

| Before | After |
|--------|-------|
| `Microsoft.EntityFrameworkCore.SqlServer` | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| SQL Server connection string | PostgreSQL connection string (above) |

**Code change needed in API and Worker projects:**

```csharp
// Replace SQL Server with PostgreSQL:
builder.AddNpgsqlDbContext<SignalStackDbContext>("Postgres");
```

### 1.5 Supabase Studio (Built-in Admin UI)

Supabase comes with a web-based SQL editor, table browser, and API explorer.

- **Table Editor** — browse and edit data visually
- **SQL Editor** — run ad-hoc queries
- **API Docs** — auto-generated REST and GraphQL APIs

---

## Step 2: MongoDB Atlas (Free Tier)

### 2.1 Create an Atlas Cluster

1. Go to [https://www.mongodb.com/atlas](https://www.mongodb.com/atlas) and sign up (GitHub OAuth recommended)
2. Click **Build a Database** → select **FREE** (M0) tier
3. Configure:

   | Setting | Value |
   |---------|-------|
   | **Provider** | AWS, GCP, or Azure (any) |
   | **Region** | Choose closest to you (e.g., `Mumbai — ap-south-1`) |
   | **Cluster Tier** | **M0 Sandbox** (free, 512 MB storage, shared RAM) |
   | **Cluster Name** | `SignalStack` |

4. Click **Create Cluster** (takes ~5-10 minutes to provision)

### 2.2 Set Up Database Access

1. Go to **Security → Database Access**
2. Click **Add New Database User**
3. Create a user:

   | Setting | Value |
   |---------|-------|
   | **Authentication Method** | Password |
   | **Username** | `signalstack` |
   | **Password** | Generate a strong password (save this) |
   | **Database User Privileges** | **Read and write to any database** |

### 2.3 Configure Network Access

1. Go to **Security → Network Access**
2. Click **Add IP Address**
3. Add `0.0.0.0/0` (allow all — Railway uses dynamic IPs). For a POC with a strong password, this is acceptable.
4. Click **Confirm**

### 2.4 Get Your Connection String

1. Go to **Database → Connect** → **Drivers**
2. Select **C# / .NET**
3. Copy the connection string:

   ```
   mongodb+srv://signalstack:<password>@cluster0.xxxxx.mongodb.net/signalstack?retryWrites=true&w=majority
   ```

### 2.5 Atlas UI (Built-in Admin)

- **Data Explorer** — browse collections and documents
- **Performance Advisor** — index recommendations
- **Real-time metrics** — operations/sec, latency, connections

---

## Step 3: New Relic (Free Observability — Direct OTLP Export)

No collector sidecar needed. The .NET OTel SDK is configured to export directly to New Relic with the license key in the HTTP header.

### 3.1 Sign Up

1. Go to [https://newrelic.com/signup](https://newrelic.com/signup) — select **Free forever** tier
2. After logging in, go to **Add Data** → **OpenTelemetry**
3. Note your **OTLP endpoint** and **license key**:
   - **OTLP endpoint:** `https://otlp.eu01.nr-data.net:443` (or `https://otlp.nr-data.net:443` for US)
   - **License key:** Looks like `eu01xx...`

### 3.2 Free Tier Limits

| Telemetry Type | Free Limit |
|----------------|-----------|
| Logs | 100 GB/month |
| Traces | 100 GB/month |
| Metrics | 10,000 metrics/month |
| Data retention | 8 days |

### 3.3 .NET Configuration

In the API and Worker code, the OTel setup needs two values as environment variables:

| Variable | Value |
|----------|-------|
| `NewRelic__Endpoint` | `https://otlp.eu01.nr-data.net:443` |
| `NewRelic__LicenseKey` | `eu01xx...` |

The .NET code configures the exporter once at startup:

```csharp
// Program.cs — simplified, no collector needed
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                Environment.GetEnvironmentVariable("NewRelic__Endpoint")
                ?? "https://otlp.eu01.nr-data.net:443");
            options.Headers = "api-key=" +
                Environment.GetEnvironmentVariable("NewRelic__LicenseKey");
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                Environment.GetEnvironmentVariable("NewRelic__Endpoint")
                ?? "https://otlp.eu01.nr-data.net:443");
            options.Headers = "api-key=" +
                Environment.GetEnvironmentVariable("NewRelic__LicenseKey");
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
        }));
```

> **Why HTTP Protobuf over gRPC:** New Relic's OTLP endpoint accepts both, but HTTP makes it easy to pass the `api-key` header. No local collector needed.

---

## Step 4: CI/CD

### 4.1 CI Workflow

The project has a CI workflow in `.github/workflows/ci.yml` that validates builds and tests on every PR and push.

### 4.2 Build and Deploy (Railway Auto-Deploy)

Build and deployment of all three services (api, worker, web) is handled entirely by **Railway's own GitHub integration**:

1. Connect your GitHub repo to Railway in the Railway dashboard
2. Railway watches the repo and auto-deploys each service on every push to the linked branch
3. No separate GitHub Actions deploy workflow is needed — Railway builds from the Dockerfiles and deploys internally

### 4.3 sys_config Seeder (Pre-Deploy)

A separate GitHub Actions workflow (`.github/workflows/deploy-railway.yml`, manual trigger via `workflow_dispatch`) runs the `sys_config` seeder against MongoDB to ensure required configuration rows exist before the services start. This is a data seeding step, not a deployment step.

### 4.4 Web (Next.js)

The Web service is also deployed via Railway (same project, alongside API and Worker). No separate Vercel deployment needed.

### 4.5 Required GitHub Secrets

| Secret | Value | Used By |
|--------|-------|---------|
| `MONGO_CONNECTION_STRING` | MongoDB Atlas connection string | sys_config seeder |
| `CONNECTIONSTRINGS__MONGODB` | MongoDB Atlas connection string | API/Worker (Railway env vars) |
| `CONNECTIONSTRINGS__POSTGRES` | Supabase connection string | API/Worker (Railway env vars) |
| `CONNECTIONSTRINGS__REDIS` | Redis Cloud connection string | API/Worker (Railway env vars) |
| `NewRelic__Endpoint` | New Relic OTLP endpoint URL | API/Worker (Railway env vars) |
| `NewRelic__LicenseKey` | New Relic license key | API/Worker (Railway env vars) |

---

## Step 5: Railway Setup (Web + API + Worker)

### 5.1 Sign Up and Create a Project

1. Go to [https://railway.app](https://railway.app) — sign up with your GitHub account
2. Click **New Project** → **Deploy from GitHub repo**
3. Select your `signalstack` repository
4. Railway will scan the repo and detect services. For manual setup, continue below.

### 5.2 Add Web Service

1. In your Railway project, click **New** → **Service**
2. Select **Add a service** → **GitHub repo** → select `signalstack`
3. Configure the service:

   | Setting | Value |
   |---------|-------|
   | **Service name** | `web` |
   | **Root directory** | `.` (repo root) |
   | **Dockerfile path** | `apps/web/Dockerfile` |
   | **Start command** | (leave empty — Dockerfile has CMD) |

4. Add Environment Variables:

   | Variable | Value |
   |----------|-------|
   | `NEXT_PUBLIC_API_BASE_URL` | The Railway API service URL (e.g., `https://api-production-xxxx.up.railway.app`) |

### 5.3 Add API Service

1. In your Railway project, click **New** → **Service**
2. Select **Add a service** → **GitHub repo** → select `signalstack`
3. Configure the service:

   | Setting | Value |
   |---------|-------|
   | **Service name** | `api` |
   | **Root directory** | `.` (repo root) |
   | **Dockerfile path** | `apps/api/Dockerfile` |
   | **Start command** | (leave empty — Dockerfile has ENTRYPOINT) |

   > **Important:** Root directory must be `.` (repo root), not `apps/api`. The Dockerfile
   > `COPY packages/ packages/` step needs access to the `packages/` directory at the repo root.
   > Setting root directory to `apps/api` limits the build context to that subdirectory, causing
   > "project not found" errors for all shared library references.

4. Add Environment Variables (click on the service → **Variables**):

   | Variable | Value |
   |----------|-------|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `ConnectionStrings__MongoDb` | Your MongoDB Atlas connection string |
   | `ConnectionStrings__Postgres` | Your Supabase connection string |
   | `ConnectionStrings__Redis` | Your Redis Cloud connection string |
   | `NewRelic__Endpoint` | `https://otlp.eu01.nr-data.net:443` |
   | `NewRelic__LicenseKey` | Your New Relic license key |

5. Railway assigns a public URL like `https://api-production-xxxx.up.railway.app` — auto-HTTPS, no setup needed.

### 5.4 Add Worker Service

1. Click **New** → **Service** → select the same repo
2. Configure:

   | Setting | Value |
   |---------|-------|
   | **Service name** | `worker` |
   | **Root directory** | `.` (repo root) |
   | **Dockerfile path** | `apps/worker/Dockerfile` |
   | **Start command** | (leave empty — Dockerfile has ENTRYPOINT) |

   > **Important:** Same as API — root directory must be `.` for the `COPY packages/` step to work.

3. Add the same environment variables as the API service (Railway supports sharing variables across services if set at the project level, or you can add per-service)

4. **Important:** The Worker doesn't need a public port. Railway doesn't expose worker services to the internet by default.

### 5.5 Auto-deploy

By default, Railway deploys every push to the linked branch. To configure:

1. Go to your service → **Settings** → **Deploy**
2. **Auto Deploy** should be **On** (default)
3. **Deploy Branch** — set to `main` (or your default branch)

### 5.6 Service URLs

| Service | URL |
|---------|-----|
| **Web** | `https://web-production-xxxx.up.railway.app` (auto-HTTPS) |
| **API** | `https://api-production-xxxx.up.railway.app` (auto-HTTPS) |
| **Worker** | No public URL (internal only) |

### 5.7 Railway Dashboard

- **Metrics** — CPU, memory, network per service
- **Logs** — real-time and historical logs per service
- **Deployments** — deployment history with rollback
- **Domains** — auto-assigned `*.railway.app` URL, custom domains optional

---

## Complete Connection String Reference

| Service | Connection String |
|---------|------------------|
| **MongoDB (Atlas)** | `mongodb+srv://<user>:<password>@<cluster>.mongodb.net/signalstack?retryWrites=true&w=majority` |
| **PostgreSQL (Supabase)** | `Host=<ref>.supabase.co;Port=5432;Database=signalstack;Username=postgres;Password=<password>;SSL Mode=Require;Trust Server Certificate=true;` |
| **Redis (Redis Cloud)** | `redis://:<password>@<host>:<port>` |
| **New Relic Endpoint** | `https://otlp.eu01.nr-data.net:443` (set in code + env var) |

---

## First Launch Checklist

### Setup completed:

- [x] **Supabase project** created — PostgreSQL connection string saved
- [x] **MongoDB Atlas cluster** created — connection string saved
- [x] **Redis Cloud account** configured — 50 MB free tier, connection string saved

### Deployment steps:

- [ ] Railway account connected to GitHub
- [ ] Railway project created with Web service (`apps/web/Dockerfile`)
- [ ] Railway project created with API service (`apps/api/Dockerfile`)
- [ ] Railway project created with Worker service (`apps/worker/Dockerfile`)
- [ ] Environment variables set on all three Railway services
- [ ] MongoDB Atlas network access allows `0.0.0.0/0` (Railway dynamic IPs)
- [ ] Web env var `NEXT_PUBLIC_API_BASE_URL` set to Railway API URL
- [ ] API responds to health check at `https://api-xxxx.up.railway.app/health`
- [ ] Worker starts and shows "acquired lease" in Railway logs
- [ ] Google OAuth redirect URI registered as `https://api-xxxx.up.railway.app/api/v1/auth/google/callback`
- [ ] FYERS app redirect URI registered as `https://api-xxxx.up.railway.app/api/v1/auth/fyers/callback`
- [ ] New Relic OTLP endpoint configured — telemetry flowing
- [ ] GitHub Actions CI passes on push to main

---

## Migration Path to Production

When you're ready to move beyond evaluation:

| Component | Production Target | Migration Effort |
|-----------|-----------------|-----------------|
| **API** | Azure Container Apps | Minimal (same Dockerfile) |
| **Worker** | Azure Container Apps | Minimal (same Dockerfile) |
| **Web** | Azure Static Web Apps | Minimal (same Dockerfile) |
| **PostgreSQL** | Azure Database for PostgreSQL | Dump & restore from Supabase |
| **MongoDB** | Azure Cosmos DB for MongoDB | Connection string change |
| **Redis** | Azure Cache for Redis | Connection string change |
| **Observability** | Azure Application Insights | SDK swap (similar API) |

The architecture is cloud-agnostic by design. Railway → Azure is a config change, not a rewrite.
