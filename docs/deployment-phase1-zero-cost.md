# Phase 1 Deployment — $0/Month Architecture (Supabase + Atlas + Railway)

> **Update (May 2026):** Replaced Oracle Cloud VM with managed free-tier services. API + Worker hosted on Railway (auto-HTTPS, no Caddy/domain needed). Databases fully managed (Supabase, MongoDB Atlas, Redis Cloud). OTel Collector removed — .NET SDK exports directly to New Relic.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Vercel (Free) — signalstack.vercel.app                 │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Web (Next.js) — no business logic               │   │
│  └──────────────────────────────────────────────────┘   │
│             │ HTTPS calls                                │
│             ▼                                            │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Railway Project — signalstack                    │   │
│  │                                                   │   │
│  │  ┌──────────────────┐  ┌──────────────────┐      │   │
│  │  │ API Service       │  │ Worker Service    │      │   │
│  │  │ (.NET)            │  │ (.NET background) │      │   │
│  │  │ *.railway.app    │  │ (no public port)   │      │   │
│  │  │ auto HTTPS        │  │ always-on         │      │   │
│  │  └───────┬──────────┘  └────────┬─────────┘      │   │
│  │          │                      │                  │   │
│  └──────────┼──────────────────────┼──────────────────┘   │
└─────────────┼──────────────────────┼──────────────────────┘
              │                      │
              ▼                      ▼
┌──────────────────────┐  ┌──────────────────────┐  ┌──────────────────────┐
│  MongoDB Atlas M0    │  │  Supabase Free        │  │  Redis Cloud Free    │
│  (Free Tier)         │  │  (PostgreSQL)         │  │  (redis.com)         │
│  Nifty 500 data      │  │  OHLCV historical     │  │  Caching, sessions   │
│  512MB storage       │  │  500MB database       │  │  50MB                │
└──────────────────────┘  └──────────────────────┘  └──────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  New Relic Free (100 GB/mo logs, 100 GB/mo traces,      │
│   10k metrics/month)                                     │
│  Direct OTLP export from .NET SDK — no collector         │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  GitHub Actions — CI/CD (2000-3000 min/month free)      │
│  Auto-deploy to Railway and Vercel on push to main      │
└─────────────────────────────────────────────────────────┘
```

## Cost Breakdown

| Component | Service | Monthly Cost | Notes |
|-----------|---------|-------------|-------|
| **Web** (Next.js) | Vercel Hobby | **$0** | 100 GB bandwidth, 6000 build min/mo |
| **API** (.NET) | Railway | **$5-10** | Docker service, always-on, auto-HTTPS |
| **Worker** (.NET) | Railway | **included** | Second service in same Railway project |
| **MongoDB** | MongoDB Atlas M0 | **$0** | 512 MB shared storage, free forever |
| **PostgreSQL** | Supabase Free | **$0** | 500 MB database, 2 GB bandwidth |
| **Redis** | Redis Cloud Free | **$0** | 50 MB, redis.com |
| **Observability** | New Relic Free | **$0** | Direct OTLP — URL + license key only |
| **CI/CD** | GitHub Actions | **$0** | 2000-3000 min/month free |
| **Domain** | Not needed | **$0** | Railway provides `*.railway.app`, Vercel provides `*.vercel.app` |
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

## Step 4: CI/CD — GitHub Actions

### 4.1 Workflow

The project already has workflows in `.github/workflows/`. The CI pipeline validates builds and tests on every PR and push:

```yaml
# .github/workflows/ci.yml
name: CI
on:
  pull_request:
    branches: [main]
  push:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release
```

### 4.2 Deploy to Railway (Auto-deploy from GitHub)

Railway's GitHub integration handles deployment:

1. Connect your GitHub repo to Railway
2. Railway auto-deploys each service on every push to the linked branch
3. No separate deploy workflow needed in GitHub Actions

**Alternative:** If you want the deploy step visible in Actions, add this deploy workflow:

```yaml
# .github/workflows/deploy-railway.yml
name: Deploy to Railway
on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: railway/railway-action@v3
        with:
          railway_token: ${{ secrets.RAILWAY_TOKEN }}
```

The `RAILWAY_TOKEN` is generated from Railway dashboard → Account → Tokens.

### 4.3 Deploy to Vercel

Vercel auto-deploys from GitHub — just connect your repo in the Vercel dashboard. No workflow needed.

### 4.4 Required GitHub Secrets

| Secret | Value | Used By |
|--------|-------|---------|
| `RAILWAY_TOKEN` | Railway deploy token (optional — Railway auto-deploy works without it) | Deploy workflow |
| `CONNECTIONSTRINGS__MONGODB` | MongoDB Atlas connection string | API/Worker |
| `CONNECTIONSTRINGS__POSTGRES` | Supabase connection string | API/Worker |
| `CONNECTIONSTRINGS__REDIS` | Redis Cloud connection string | API/Worker |
| `NewRelic__Endpoint` | New Relic OTLP endpoint URL | API/Worker |
| `NewRelic__LicenseKey` | New Relic license key | API/Worker |

---

## Step 5: Vercel (Web — Next.js)

### 5.1 Deploy

1. Go to [https://vercel.com](https://vercel.com) — sign up with your GitHub account
2. Click **Import Project** → Select your `signalstack` repo
3. Configure:

   | Setting | Value |
   |---------|-------|
   | Framework Preset | Next.js |
   | Root Directory | `apps/web` |
   | Build Command | `npm run build` (default) |
   | Output Directory | `.next` (default) |

4. Add Environment Variables:

   | Name | Value |
   |------|-------|
   | `NEXT_PUBLIC_API_BASE_URL` | The Railway API service URL (e.g., `https://api-production-xxxx.up.railway.app`) |

5. Click **Deploy**

### 5.2 Custom Domain (Optional)

Vercel gives you `signalstack.vercel.app` for free. No custom domain needed.

---

## Step 6: Railway Setup (API + Worker)

### 6.1 Sign Up and Create a Project

1. Go to [https://railway.app](https://railway.app) — sign up with your GitHub account
2. Click **New Project** → **Deploy from GitHub repo**
3. Select your `signalstack` repository
4. Railway will scan the repo and detect services. For manual setup, continue below.

### 6.2 Add API Service

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

### 6.3 Add Worker Service

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

### 6.4 Auto-deploy

By default, Railway deploys every push to the linked branch. To configure:

1. Go to your service → **Settings** → **Deploy**
2. **Auto Deploy** should be **On** (default)
3. **Deploy Branch** — set to `main` (or your default branch)

### 6.5 Service URLs

| Service | URL |
|---------|-----|
| **API** | `https://api-production-xxxx.up.railway.app` (auto-HTTPS) |
| **Worker** | No public URL (internal only) |

### 6.6 Railway Dashboard

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
- [ ] Railway project created with API service (`apps/api/Dockerfile`)
- [ ] Railway project created with Worker service (`apps/worker/Dockerfile`)
- [ ] Environment variables set on both Railway services
- [ ] MongoDB Atlas network access allows `0.0.0.0/0` (Railway dynamic IPs)
- [ ] API responds to health check at `https://api-xxxx.up.railway.app/health`
- [ ] Worker starts and shows "acquired lease" in Railway logs
- [ ] Vercel project created and builds successfully
- [ ] Vercel env var `NEXT_PUBLIC_API_BASE_URL` set to Railway API URL
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
| **Web** | Vercel / Azure Static Web Apps | Zero (already on Vercel) |
| **PostgreSQL** | Azure Database for PostgreSQL | Dump & restore from Supabase |
| **MongoDB** | Azure Cosmos DB for MongoDB | Connection string change |
| **Redis** | Azure Cache for Redis | Connection string change |
| **Observability** | Azure Application Insights | SDK swap (similar API) |

The architecture is cloud-agnostic by design. Railway → Azure is a config change, not a rewrite.
