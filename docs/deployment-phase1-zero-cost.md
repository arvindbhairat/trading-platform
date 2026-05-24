# Phase 1 Deployment — $0/Month Architecture (Supabase + Atlas + Vercel)

> **Update (May 2026):** Replaced Oracle Cloud VM with managed free-tier services after Oracle's convoluted setup became a blocker. Databases are now fully managed (Supabase, MongoDB Atlas). API/Worker compute hosting is TBD and will be added when unparked.

## Architecture (Current — Databases + Observability Only)

```
┌─────────────────────────────────────────────────────────┐
│  Vercel (Free) — signalstack.vercel.app                 │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Web (Next.js) — no business logic               │   │
│  └──────────────────────────────────────────────────┘   │
│             │ HTTPS calls                                │
│             ▼                                            │
│  ┌──────────────────────────────────────────────────┐   │
│  │  API (ASP.NET Core) — hosting TBD                │   │
│  │  (will be added when unparked)                   │   │
│  └──────────────────────────────────────────────────┘   │
│                                                         │
└─────────────────────────────────────────────────────────┘

┌──────────────────────┐  ┌──────────────────────┐  ┌──────────────────────┐
│  MongoDB Atlas M0    │  │  Supabase Free        │  │  Redis Cloud Free    │
│  (Free Tier)         │  │  (PostgreSQL)         │  │  (redis.com)         │
│  Nifty 500 data      │  │  OHLCV historical     │  │  Caching, sessions   │
│  512MB storage       │  │  500MB database       │  │  50MB                │
└──────────────────────┘  └──────────────────────┘  └──────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  New Relic Free (100 GB/mo logs, 100 GB/mo traces,      │
│   10k metrics/month)                                     │
│  Direct OTLP/HTTP export — no collector sidecar needed   │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  GitHub Actions — CI/CD (2000-3000 min/month free)      │
└─────────────────────────────────────────────────────────┘
```

> **Note on OTLP Export:** The .NET apps now export OTLP/HTTP directly to the New Relic endpoint, passing the license key in the `api-key` header. No intermediate collector sidecar is needed.

## Cost Breakdown

| Component | Service | Monthly Cost | Notes |
|-----------|---------|-------------|-------|
| **Web** (Next.js) | Vercel Hobby | **$0** | 100 GB bandwidth, 6000 build min/mo |
| **API** (.NET 10) | TBD | **TBD** | Parked — options below |
| **Worker** (.NET 10) | TBD | **TBD** | Parked — options below |
| **MongoDB** | MongoDB Atlas M0 | **$0** | 512 MB shared storage, free forever |
| **PostgreSQL** | Supabase Free | **$0** | 500 MB database, 2 GB bandwidth |
| **Redis** | Redis Cloud Free | **$0** | 50 MB, redis.com — user's existing account |
| **Observability** | New Relic Free | **$0** | 100 GB/mo logs, 100 GB/mo traces, 10k metrics |
| **CI/CD** | GitHub Actions | **$0** | 2000-3000 min/month free |
| **Domain** | `.in` or `.dev` | **~$0.80** | ~₹65/month (~$10/year) |
| **Total (current)** | | **~$0.80/month** | Databases + observability only |
| **Total (with compute)** | | **~$5-10/month** | Adding Hetzner VM or equivalent |

---

## Step 1: Supabase (PostgreSQL — replaces SQL Server)

### What Changed

The original architecture called for SQL Server. Supabase provides fully managed PostgreSQL on a free tier — zero setup, zero maintenance, just a connection string.

Supabase free tier limits:
- **500 MB database** — ample for Nifty 500 OHLCV (daily data for 500 stocks × 10 years ≈ 40-50 MB)
- **2 GB bandwidth**
- **50,000 monthly active users**
- **Automatic backups** and point-in-time recovery

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
2. Under **Connection string**, find the **URI** entry. It looks like:

   ```
   postgresql://postgres:<password>@db.<ref>.supabase.co:5432/postgres
   ```

3. Replace `<password>` with the database password you set during creation
4. Replace the default database name `postgres` with `signalstack` (or create a new database)
5. **Important:** Supabase enforces SSL/TLS for external connections. EF Core / Npgsql handles this automatically when you set `SSL Mode=Require` in the connection string.

### 1.3 Connection String for .NET Configuration

For the app's `appsettings.Production.json` or environment variable:

```
Host=db.<ref>.supabase.co;Port=5432;Database=signalstack;Username=postgres;Password=<password>;SSL Mode=Require;Trust Server Certificate=true;
```

### 1.4 Schema Management

Since Supabase is PostgreSQL (not SQL Server), the EF Core provider changes:

| Before | After |
|--------|-------|
| `Microsoft.EntityFrameworkCore.SqlServer` | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| SQL Server connection string | PostgreSQL connection string (above) |

**Code change needed** in the API and Worker projects:

```csharp
// Before (SQL Server):
builder.AddNpgsqlDbContext<SignalStackDbContext>("SqlServer");

// After (PostgreSQL):
builder.AddNpgsqlDbContext<SignalStackDbContext>("Postgres");
```

### 1.5 Supabase Studio (Built-in Admin UI)

Supabase comes with a web-based SQL editor, table browser, and API explorer — no need for SQL Server Management Studio or Azure Data Studio.

- **Table Editor** — browse and edit data visually
- **SQL Editor** — run ad-hoc queries
- **API Docs** — auto-generated REST and GraphQL APIs
- **Database Migrations** — apply raw SQL migrations if needed

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
   | **Database User Privileges** | **Read and write to any database** (or restrict to `signalstack` DB) |

### 2.3 Configure Network Access

1. Go to **Security → Network Access**
2. Click **Add IP Address**
3. For Phase 1 (when API/Worker hosting is decided), you have two options:

   - **Option A:** `0.0.0.0/0` (allow all — simpler but less secure; appropriate for POC with a strong password)
   - **Option B:** Add the specific IP of wherever API/Worker will be hosted

4. Click **Confirm**

### 2.4 Get Your Connection String

1. Go to **Database → Connect** → **Drivers**
2. Select **C# / .NET** as the driver
3. Copy the connection string:

   ```
   mongodb+srv://signalstack:<password>@cluster0.xxxxx.mongodb.net/?retryWrites=true&w=majority
   ```

### 2.5 Connection String for .NET Configuration

```
mongodb+srv://signalstack:<password>@cluster0.xxxxx.mongodb.net/signalstack?retryWrites=true&w=majority
```

### 2.6 Atlas UI (Built-in Admin)

- **Data Explorer** — browse collections and documents
- **Performance Advisor** — index recommendations
- **Real-time metrics** — operations/sec, latency, connections

---

## Step 3: New Relic (Free Observability)

### 3.1 Sign Up

1. Go to [https://newrelic.com/signup](https://newrelic.com/signup) — select **Free forever** tier
2. Complete signup (no credit card required for free tier)
3. After logging in, go to **Add Data** → **OpenTelemetry**
4. Find your **OTLP endpoint** and **license key**:
   - **OTLP endpoint:** `otlp.eu01.nr-data.net:443` (or `otlp.nr-data.net:443` for US region)
   - **License key:** Looks like `eu01xx...`

### 3.2 Free Tier Limits

| Telemetry Type | Free Limit |
|----------------|-----------|
| Logs | 100 GB/month |
| Traces | 100 GB/month |
| Metrics | 10,000 metrics/month |
| Data retention | 8 days |

These limits are more than sufficient for a solo POC.

### 3.3 .NET Configuration

The .NET apps export OTLP/HTTP directly to the New Relic endpoint. No collector sidecar is needed. When API/Worker compute hosting is set up, configure the following environment variables:

| Variable | Value |
|----------|-------|
| `Telemetry__Otlp__Endpoint` | `https://otlp.eu01.nr-data.net:443` (or `https://otlp.nr-data.net:443` for US region) |
| `Telemetry__Otlp__ApiKey` | Your New Relic license key (e.g. `eu01xx...`) |

When `Endpoint` is left empty (local dev default), OTLP export is disabled — logs still go to console via Serilog.

Save your New Relic license key somewhere safe — you'll need it when the API and Worker are deployed.

---

## Step 4: CI/CD — GitHub Actions

The project already has GitHub Actions workflows in `.github/workflows/`. For the current phase (databases only), the CI pipeline validates:

1. **Code builds** — verify both API and Worker compile
2. **Tests pass** — run unit tests on PR
3. **Linting** — static analysis

### 4.1 Required GitHub Secrets

| Secret | Value | Status |
|--------|-------|--------|
| `MONGODB_ATLAS_URI` | MongoDB Atlas connection string | Create when CI needs connectivity |
| `SUPABASE_CONNECTION_STRING` | Supabase PostgreSQL connection string | Create when CI needs connectivity |
| `NEW_RELIC_LICENSE_KEY` | Your New Relic license key | Create when apps deploy |

### 4.2 CI Workflow (Updated)

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

### 4.3 Deploy Workflow (When Compute Is Ready)

The deploy workflow will be updated when the API/Worker hosting provider is chosen. Current options:

- **Hetzner VM** — SSH + Docker Compose (similar to original Oracle approach)
- **Railway / Fly.io** — Dockerfile push with `flyctl` or `railway` CLI
- **Azure Container Apps** — `az` CLI deploy

The workflow will be adjusted based on whichever provider is chosen.

---

## Step 5: Vercel (Web — Next.js)

Unchanged from the original plan. Vercel is the native hosting platform for Next.js and requires no VM.

### 5.1 Deploy

1. Go to [https://vercel.com](https://vercel.com)
2. Sign up with your GitHub account
3. Click **Import Project** → Select your `signalstack` repo
4. Configure:

   | Setting | Value |
   |---------|-------|
   | Framework Preset | Next.js |
   | Root Directory | `apps/web` |
   | Build Command | `npm run build` (default) |
   | Output Directory | `.next` (default) |

5. Add Environment Variables:

   | Name | Value |
   |------|-------|
   | `NEXT_PUBLIC_API_BASE_URL` | `https://api.yourdomain.com` (TBD until API is hosted) |

6. Click **Deploy**

### 5.2 Custom Domain

1. In Vercel dashboard → your project → **Settings** → **Domains**
2. Add `www.yourdomain.com` (optional — can defer until API is ready)

---

## Step 6: API & Worker Hosting (TBD — Options When Ready)

These are parked for now. When you're ready to deploy them, here are the recommended options ranked by ease of setup:

### Option A: Hetzner CX22 (~€3.99/month) — Recommended for Ease of Setup

One VM running Docker Compose with all services. Same architecture as the original Oracle plan, but on a platform that actually works well.

| Spec | Value |
|------|-------|
| vCPU | 2 |
| RAM | 4 GB |
| Storage | 40 GB (NVMe SSD) |
| Cost | **€3.99/month** (~$5) |
| Setup | SSH in, `docker compose up` |

**What runs on it:**
- API container
- Worker container
- Caddy (HTTPS reverse proxy)
- (Databases are managed — Supabase + Atlas + Redis Cloud)

**Pros:** Single dashboard, single SSH, everything in one Docker compose, 4 GB RAM is plenty since databases are managed off-box.

**Setup:** Same as the original Oracle doc's Steps 2-3, minus the database containers (they're managed now).

### Option B: Hetzner CX32 (~€7.99/month) — Headroom

Double the capacity if you want breathing room.

| Spec | Value |
|------|-------|
| vCPU | 4 |
| RAM | 8 GB |
| Storage | 80 GB |
| Cost | **€7.99/month** (~$9) |

### Option C: Railway / Fly.io / Render

Dockerfile-based PaaS — less to manage, but each service is separate (API one service, Worker another). Good if you prefer managed compute, but more dashboards to juggle.

| Service | Free Tier | Notes |
|---------|-----------|-------|
| **Railway** | $5 credit/month | ~$0.20/hr for .NET — might need $5-10/mo |
| **Fly.io** | 3 shared VMs, 256 MB RAM each | Just enough for API + Worker |
| **Render** | Free (spins down after inactivity) | Not suitable for always-on Worker |

### Option D: Azure Container Apps (When Ready to Migrate to Cloud)

The eventual production target. ~$15-30/month for a minimal setup with managed Postgres/MongoDB/Redis.

---

## Complete Connection String Reference

When configuring the API and Worker, these are the connection strings:

| Service | Format |
|---------|--------|
| **MongoDB (Atlas)** | `mongodb+srv://<user>:<password>@<cluster>.mongodb.net/signalstack?retryWrites=true&w=majority` |
| **PostgreSQL (Supabase)** | `Host=<ref>.supabase.co;Port=5432;Database=signalstack;Username=postgres;Password=<password>;SSL Mode=Require;Trust Server Certificate=true;` |
| **Redis (Redis Cloud)** | `redis://:<password>@<host>:<port>` (get from Redis Cloud dashboard) |
| **OTLP Endpoint** | `Telemetry__Otlp__Endpoint` environment variable (direct to New Relic, no collector) |

---

## First Launch Checklist

### Setup completed:

- [x] **Supabase project** created — PostgreSQL connection string saved
- [x] **MongoDB Atlas cluster** created — connection string saved
- [x] **Redis Cloud account** configured — 50 MB free tier, connection string saved
- [ ] New Relic free account set up — license key saved

### When compute hosting is ready:

- [ ] VM or PaaS provisioned for API + Worker
- [ ] Docker installed (if using VM)
- [ ] Domain DNS pointing to compute host
- [ ] Caddy or equivalent reverse proxy configured with HTTPS
- [ ] Google OAuth created with correct redirect URI
- [ ] FYERS app updated with correct redirect URI
- [ ] `.env` populated on the host with all secrets
- [ ] OTLP endpoint and API key configured for New Relic
- [ ] API responds to health check
- [ ] Worker acquires lease and starts processing
- [ ] Vercel project connected to repo and builds successfully
- [ ] Telemetry flowing to New Relic

---

## Summary: Before vs After

| Area | Old (Oracle VM) | New (Managed Services) |
|------|----------------|----------------------|
| **Compute** | Oracle VM (4 ARM, 24 GB) | **TBD** (Hetzner, Railway, or equivalent) |
| **PostgreSQL** | Docker on Oracle VM | **Supabase Free** — managed, auto-backups |
| **MongoDB** | Docker on Oracle VM | **Atlas M0 Free** — managed, auto-backups |
| **Redis** | Docker on Oracle VM | **Redis Cloud Free** — managed, 50 MB |
| **Web hosting** | Vercel Free | **Vercel Free** (unchanged) |
| **Observability** | New Relic Free | **New Relic Free** (unchanged) |
| **CI/CD** | GitHub Actions | **GitHub Actions** (unchanged) |
| **Setup pain** | **High** (Oracle console, networking, account blocks) | **Low** (Supabase + Atlas = 5 minutes each) |
| **Total cost** | ~$0.80/month | **~$0.80/month** (current) or **~$5-10/month** (+ compute) |
| **Backups** | DIY cron scripts | **Built-in** (managed databases) |

**Bottom line:** For ~$5-6/month total (adding a Hetzner VM when compute is needed), you get a fully managed database layer that requires zero maintenance, and a single Docker box for the app layer that takes minutes to set up. No Oracle Cloud headaches.
