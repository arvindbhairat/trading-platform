# Phase 1 Deployment — $0/Month Architecture (Oracle VM + Vercel + Free Observability)

## Architecture (Corrected for Bootstrap Config)

> **Important finding:** The code's bootstrap configuration system REQUIRES local JSON snapshot files to exist at startup, or the app crashes. Environment variables don't bypass this. And the OTLP exporter uses **gRPC protocol** (not HTTP) and doesn't set auth headers — so apps can't connect directly to cloud providers. The fix: run a local **OTel Collector** container that receives unauthenticated gRPC on port 4317 and forwards with auth to the cloud provider. This matches the architecture intent in `docs/system-architecture.md` (a collector fans telemetry out to a cloud observability backend).

```
┌─────────────────────────────────────────────────────────┐
│  Vercel (Free) — signalstack.vercel.app                 │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Web (Next.js) — no business logic               │   │
│  └──────────────────────────────────────────────────┘   │
│             │ HTTPS calls                                │
│             ▼                                            │
│  ┌──────────────────────────────────────────────────┐   │
│  │  API (ASP.NET Core)  api.yourdomain.com          │   │
│  │  sends OTLP gRPC → otel-collector:4317           │   │
│  └──────────────────────────────────────────────────┘   │
│                                                         │
│         ┌─────── Same docker compose network ──────┐    │
│         ▼                                           │    │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  Oracle Cloud VM — All containers in one Docker network │
│                                                         │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐              │
│  │ Worker   │  │ API      │  │ Web      │  ← not used  │
│  │ (.NET)   │  │ (.NET)   │  │ container│  (Vercel)    │
│  └────┬─────┘  └────┬─────┘  └──────────┘              │
│       │             │                                    │
│       ▼             ▼                                    │
│  ┌──────────────────────────────────────────────────┐   │
│  │  OTel Collector (gRPC :4317, internal only)      │   │
│  │  ┌─ exporters: ───────────────────────────────┐  │   │
│  │  │  adds auth header → forwards to cloud      │  │   │
│  │  └────────────────────────────────────────────┘  │   │
│  └──────────────────────────────────────────────────┘   │
│                                                         │
│  ┌────────┐  ┌────────┐  ┌────────┐  ┌──────────────┐  │
│  │ SQL    │  │ MongoDB│  │ Redis  │  │ Caddy (443)  │  │
│  │ Express│  │ 7      │  │ 7      │  │ Let's Encrypt│  │
│  └────────┘  └────────┘  └────────┘  └──────────────┘  │
│                                                         │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│  New Relic (Free Tier)                                  │
│  Receives OTLP from collector, not from apps directly   │
└─────────────────────────────────────────────────────────┘
```

## Cost Breakdown

| Component | Service | Monthly Cost | Notes |
|-----------|---------|-------------|-------|
| **Web** (Next.js) | Vercel Hobby | **$0** | 100 GB bandwidth, 6000 build min/mo |
| **API** (.NET 10) | Oracle VM (Docker) | **$0** | 4 ARM cores, 24 GB RAM |
| **Worker** (.NET 10) | Oracle VM (Docker) | **$0** | Always-on for market hours |
| **MongoDB** | Oracle VM (Docker) | **$0** | Tuned for Nifty 500 data volume |
| **SQL Server** | Oracle VM — Docker Express | **$0** | 10 GB limit — ample for Phase 1 |
| **Redis** | Oracle VM (Docker) | **$0** | 7-alpine, append-only |
| **HTTPS** | Caddy + Let's Encrypt | **$0** | Auto-renewing |
| **Observability** | New Relic Free | **$0** | 100 GB/mo logs, 100 GB/mo traces, 10k metrics |
| **CI/CD** | GitHub Actions | **$0** | 2000-3000 min/month free |
| **Domain** | `.in` or `.dev` | **~$0.80** | ~₹65/month (~$10/year) |
| **Total** | | **~$0.80/month** | |

---

## Step 1: Oracle Cloud Free Account + VM

### 1.1 Sign Up

1. Go to [https://www.oracle.com/cloud/free/](https://www.oracle.com/cloud/free/)
2. Click **Start for free**
3. Fill in details (credit card required for identity verification — you won't be charged)
4. After signup, log in to the OCI Console

### 1.2 Create the Always Free VM

1. In the OCI Console, go to **Compute → Instances**
2. Click **Create instance**
3. **Name:** `signalstack-solo`
4. **Placement:** Select an Always Free-eligible availability domain
5. **Image:** **Canonical Ubuntu 24.04** (or Oracle Linux 8)
6. **Shape:** Select **Ampere A1** (Always Free)
   - **OCPU count:** **4** (the maximum free allocation)
   - **Memory:** **24 GB**
7. **Networking:**
   - Create a new VCN or use the default
   - **Assign a public IPv4 address:** Yes
8. **Add SSH keys:**
   - Choose **Generate a key pair for me** (download both private and public keys)
   - Or paste your existing public key
9. **Boot volume:** **200 GB** (Always Free total)
10. Click **Create**

### 1.3 Open Firewall Ports

1. Go to **Networking → Virtual Cloud Networks → your VCN**
2. Click **Security Lists → Default Security List**
3. Click **Add Ingress Rules** and add:

| Source Type | Source | IP Protocol | Destination Port | Description |
|-------------|--------|-------------|-----------------|-------------|
| CIDR | `0.0.0.0/0` | TCP | 80 | HTTP (for Let's Encrypt) |
| CIDR | `0.0.0.0/0` | TCP | 443 | HTTPS |
| CIDR | `0.0.0.0/0` | TCP | 22 | SSH (your IP only if preferred) |

---

## Step 2: VM Setup

### 2.1 SSH In

```bash
# From your machine (adjust path to your SSH key)
ssh -i ~/.ssh/oracle_key ubuntu@<VM_PUBLIC_IP>
```

### 2.2 Install Docker + Caddy

```bash
# Update system
sudo apt update && sudo apt upgrade -y

# Install Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh
sudo usermod -aG docker $USER

# Log out and back in for Docker group to take effect
exit
ssh -i ~/.ssh/oracle_key ubuntu@<VM_PUBLIC_IP>

# Install Caddy
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install caddy -y
```

### 2.3 Create the Production Docker Compose (with OTLP Collector)

On the VM, create the deploy directory:

```bash
mkdir -p ~/signalstack && cd ~/signalstack
```

Create `~/signalstack/docker-compose.yml`:

```yaml
name: signalstack-prod

services:
  # ── OTLP Collector (receives unauthenticated gRPC, forwards to cloud) ──
  # The apps hardcode OtlpExportProtocol.Grpc and don't set auth headers,
  # so they can't talk to cloud providers directly.  The collector is the
  # auth gateway — apps send to :4317 locally, collector adds the api-key
  # header and relays to New Relic.
  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.102.1
    container_name: signalstack-otel
    restart: unless-stopped
    ports:
      - "127.0.0.1:4317:4317"     # gRPC — the apps talk here (not directly to cloud)
    volumes:
      - ./otel-config.yaml:/etc/otelcol/config.yaml:ro
      - otel_data:/var/lib/otelcol
    environment:
      - NEW_RELIC_LICENSE_KEY=${NEW_RELIC_LICENSE_KEY}

  # ── Data Stores ──────────────────────────────────────
  mongo:
    image: mongo:7
    container_name: signalstack-mongo
    restart: unless-stopped
    ports:
      - "127.0.0.1:27017:27017"
    volumes:
      - mongo_data:/data/db
    command: ["--quiet"]

  redis:
    image: redis:7-alpine
    container_name: signalstack-redis
    restart: unless-stopped
    ports:
      - "127.0.0.1:6379:6379"
    volumes:
      - redis_data:/data
    command: ["redis-server", "--appendonly", "yes"]

  mssql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: signalstack-mssql
    restart: unless-stopped
    ports:
      - "127.0.0.1:1433:1433"
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "${MSSQL_SA_PASSWORD}"
    volumes:
      - mssql_data:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$${MSSQL_SA_PASSWORD}\" -Q \"SELECT 1\" -C || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s

  # ── API ──────────────────────────────────────────────
  api:
    image: signalstack-api:latest
    container_name: signalstack-api
    build:
      context: /home/ubuntu/signalstack/repo
      dockerfile: apps/api/Dockerfile
    restart: unless-stopped
    ports:
      - "127.0.0.1:5000:8080"      # Caddy proxies from port 443 → 5000
    environment:
      - ENVIRONMENT=Production
      - ASPNETCORE_ENVIRONMENT=Production
      - SEED_ADMIN_EMAIL=${SEED_ADMIN_EMAIL}
      # ── Connection strings ──
      - ConnectionStrings__MongoDb=mongodb://signalstack-mongo:27017/signalstack
      - ConnectionStrings__SqlServer=Server=mssql,1433;Database=signalstack;User=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;
      - ConnectionStrings__Redis=redis:6379
      # ── OTLP — point at local collector, NOT directly at cloud ──
      # Env vars (__ notation) take highest priority in .NET config,
      # so this overrides whatever the bootstrap snapshot files contain.
      - Telemetry__Otlp__PrimaryEndpoint=http://otel-collector:4317
      # ── Secrets (all via env vars for Phase 1) ──
      - Authentication__Google__ClientId=${GOOGLE_CLIENT_ID}
      - Authentication__Google__ClientSecret=${GOOGLE_CLIENT_SECRET}
      - Fyers__AppId=${FYERS_APP_ID}
      - Fyers__SecretKey=${FYERS_SECRET_KEY}
      - Telegram__BotToken=${TELEGRAM_BOT_TOKEN}
      - Session__SigningKey=${SESSION_SIGNING_KEY}
      - Orders__HmacKey=${ORDERS_HMAC_KEY}
    depends_on:
      otel-collector:
        condition: service_started
      mssql:
        condition: service_healthy
      mongo:
        condition: service_started
      redis:
        condition: service_started

  # ── Worker ───────────────────────────────────────────
  worker:
    image: signalstack-worker:latest
    container_name: signalstack-worker
    build:
      context: /home/ubuntu/signalstack/repo
      dockerfile: apps/worker/Dockerfile
    restart: unless-stopped
    ports: []                       # No public ports
    environment:
      - ENVIRONMENT=Production
      - ASPNETCORE_ENVIRONMENT=Production
      # ── Connection strings ──
      - ConnectionStrings__MongoDb=mongodb://signalstack-mongo:27017/signalstack
      - ConnectionStrings__SqlServer=Server=mssql,1433;Database=signalstack;User=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;
      - ConnectionStrings__Redis=redis:6379
      # ── OTLP — point at local collector ──
      - Telemetry__Otlp__PrimaryEndpoint=http://otel-collector:4317
      # ── Secrets ──
      - Fyers__AppId=${FYERS_APP_ID}
      - Fyers__SecretKey=${FYERS_SECRET_KEY}
      - Telegram__BotToken=${TELEGRAM_BOT_TOKEN}
      - Authentication__Google__ClientId=${GOOGLE_CLIENT_ID}
      - Authentication__Google__ClientSecret=${GOOGLE_CLIENT_SECRET}
    depends_on:
      otel-collector:
        condition: service_started
      mssql:
        condition: service_healthy
      mongo:
        condition: service_started
      redis:
        condition: service_started

volumes:
  otel_data:
  mongo_data:
  redis_data:
  mssql_data:
```

Create `~/signalstack/otel-config.yaml`:

```yaml
receivers:
  otlp:
    protocols:
      grpc:           # Apps send gRPC OTLP to :4317
      http:           # Also accept HTTP OTLP (for future use)

processors:
  memory_limiter:
    check_interval: 1s
    limit_mib: 128
    spike_limit_mib: 32
  batch:
    timeout: 1s
    send_batch_size: 512

exporters:
  otlp:
    endpoint: "otlp.eu01.nr-data.net:4317"      # New Relic gRPC endpoint
    headers:
      api-key: "${NEW_RELIC_LICENSE_KEY}"         # Your New Relic license key
  debug:
    verbosity: detailed                          # Also log locally for troubleshooting

service:
  pipelines:
    logs:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlp, debug]
    metrics:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlp, debug]
    traces:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlp, debug]
```

Create `~/signalstack/.env`:

```bash
# SQL Server (must have upper, lower, number, symbol, >= 8 chars)
MSSQL_SA_PASSWORD=YourStrong!Pass123

# Admin email (your Google account — will be auto-promoted to admin)
SEED_ADMIN_EMAIL=your-email@gmail.com

# FYERS
FYERS_APP_ID=your_fyers_app_id
FYERS_SECRET_KEY=your_fyers_secret_key

# Google OAuth
GOOGLE_CLIENT_ID=your_client_id.apps.googleusercontent.com
GOOGLE_CLIENT_SECRET=your_client_secret

# Telegram
TELEGRAM_BOT_TOKEN=1234567890:ABCdefGHIjklmNOPqrstUVwxyz

# Session & order signing keys (generate with: openssl rand -base64 32)
SESSION_SIGNING_KEY=base64_32_byte_key_here
ORDERS_HMAC_KEY=another_base64_32_byte_key_here

# Observability — New Relic OTLP (get this from New Relic)
NEW_RELIC_LICENSE_KEY=eu01xcd83ae386b02bfbb70153de0eed186fNRAL

# Domain
DOMAIN=api.yourdomain.com
```

### 2.4 Build and Start

**Important:** Before the API and Worker will start, they need the bootstrap snapshot files baked into their Docker images. These are in the repo at `apps/api/bootstrap/` and `apps/worker/bootstrap/`. The Dockerfiles in Appendix A include them.

```bash
cd ~/signalstack

# Pull all images
docker compose pull

# Build API and Worker (includes bootstrap files)
docker compose build api worker

# Start the OTLP collector first (so apps can connect on startup)
docker compose up -d otel-collector

# Start databases
docker compose up -d mongo redis mssql
docker compose logs mssql --tail 20   # Verify SQL Server started

# Start apps
docker compose up -d api worker

# Check all running
docker compose ps

# Follow logs
docker compose logs -f
```

---

## Step 3: HTTPS with Caddy

### 3.1 Get a Domain

You need a domain for OAuth callbacks. Options:

| Option | Cost | Provider |
|--------|------|----------|
| `.in` domain | ~₹600-800/year | GoDaddy, Namecheap, Hostinger |
| `.dev` domain | ~$12/year (~₹1,000) | Google Domains, Cloudflare |
| `.xyz` domain | ~$1/year (~₹85) | Cloudflare (first year) |
| Freenom (`.tk`, `.ml`) | Free | Freenom (limited availability) |

**Recommended:** A `.in` domain from Cloudflare (~₹600/year) — cheapest reliable option.

### 3.2 Point Domain to Your VM

In your DNS provider (e.g., Cloudflare):

| Type | Name | Value |
|------|------|-------|
| A | `api` | `<VM_PUBLIC_IP>` |

### 3.3 Configure Caddy

Create `/etc/caddy/Caddyfile`:

```caddy
api.yourdomain.com {
    reverse_proxy 127.0.0.1:5000

    # Security headers
    header /health {
        Access-Control-Allow-Origin *
    }

    header /* {
        X-Content-Type-Options nosniff
        X-Frame-Options DENY
        Referrer-Policy strict-origin-when-cross-origin
    }

    # Rate limiting is handled by the API app itself
}

# Redirect HTTP to HTTPS (automatic with Caddy)
```

Restart Caddy:

```bash
sudo systemctl restart caddy
sudo journalctl -u caddy -n 20   # Verify it started and got certs
```

Caddy automatically provisions Let's Encrypt certificates and renews them.

---

## Step 4: Deploy Web to Vercel (Free, Native Next.js)

Vercel is built by the Next.js team — it's the best possible hosting for the Web app.

### 4.1 Sign Up

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
| `NEXT_PUBLIC_API_BASE_URL` | `https://api.yourdomain.com` |

6. Click **Deploy**

**That's it.** Vercel auto-deploys on every push to main. The site is live at `signalstack.vercel.app` (or your custom domain).

### 4.2 (Optional) Custom Domain on Vercel

1. In Vercel dashboard → your project → **Settings** → **Domains**
2. Add `www.yourdomain.com` (or whatever you want users to visit)
3. Follow Vercel's DNS instructions

---

## Step 5: Free Observability — OTLP Collector + New Relic

### How It Works

The apps send OTLP data to the **local collector** (`otel-collector:4317` via gRPC). The collector adds your New Relic license key as the `api-key` header and forwards everything to New Relic's OTLP endpoint. The collector is needed because:

1. **gRPC protocol is hardcoded** (`OtlpExportProtocol.Grpc`) — the apps can't switch to HTTP
2. **No auth header support** in the exporter — the apps can't add the `api-key` header themselves

The collector handles both: it receives unauthenticated gRPC locally and adds the auth header before forwarding.

### 5.1 Set New Relic in Your .env

Add this line to `~/signalstack/.env`:

```
NEW_RELIC_LICENSE_KEY=eu01xcd83ae386b02bfbb70153de0eed186fNRAL
```

**That's it.** The collector config already references `${NEW_RELIC_LICENSE_KEY}`.

### 5.2 Verify Telemetry is Flowing

1. Wait ~2 minutes after starting the containers
2. Go to [New Relic](https://one.eu01.nr-data.net) → **APM** → **Services**
3. You should see `SignalStack.Api` and `SignalStack.Worker` appearing
4. Click into a service to see logs, metrics, and traces
5. You can also set up a **Dashboard** for key metrics like request rate, error rate, and response time

### Bypassing the Collector (Future Enhancement)

If you later modify the OpenTelemetry code to support auth headers, you could remove the collector and point apps directly at New Relic:

```csharp
// In TelemetryBootstrapExtensions.cs, add:
exporter.Headers = "api-key=eu01xcd83ae386b02bfbb70153de0eed186fNRAL";
```

For now, the collector approach works without any code changes.

---

## Step 6: Backups (Cron-based Approach)

Since databases run on a single VM (no managed backups), set up daily dumps:

```bash
# Create backup script
cat > ~/signalstack/backup.sh << 'EOF'
#!/bin/bash
BACKUP_DIR="/home/ubuntu/backups"
DATE=$(date +%Y%m%d)
RETENTION_DAYS=14

mkdir -p "$BACKUP_DIR/mongo"
mkdir -p "$BACKUP_DIR/mssql"

# MongoDB dump
docker exec signalstack-mongo mongodump --out "/tmp/mongodump-$DATE" --quiet
docker cp "signalstack-mongo:/tmp/mongodump-$DATE" "$BACKUP_DIR/mongo/"
docker exec signalstack-mongo rm -rf "/tmp/mongodump-$DATE"
gzip -f "$BACKUP_DIR/mongo/mongodump-$DATE/signalstack/*.bson"

# SQL Server dump
docker exec signalstack-mssql /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
    -Q "BACKUP DATABASE [signalstack] TO DISK='/tmp/mssql-$DATE.bak'" -C
docker cp "signalstack-mssql:/tmp/mssql-$DATE.bak" "$BACKUP_DIR/mssql/"
docker exec signalstack-mssql rm -f "/tmp/mssql-$DATE.bak"

# Cleanup old backups
find "$BACKUP_DIR/mongo" -name "*.gz" -mtime +$RETENTION_DAYS -delete
find "$BACKUP_DIR/mssql" -name "*.bak" -mtime +$RETENTION_DAYS -delete

echo "Backup complete: $DATE"
EOF

chmod +x ~/signalstack/backup.sh

# Add cron job (runs daily at 2 AM IST = 8:30 PM UTC previous day)
(crontab -l 2>/dev/null; echo "30 20 * * * /home/ubuntu/signalstack/backup.sh >> /home/ubuntu/backups/backup.log 2>&1") | crontab -
```

---

## Step 7: CI/CD

The workflow file `.github/workflows/deploy-vm.yml` already exists in the repo. It handles:

1. SSH into the VM
2. Git pull latest code
3. Build API + Worker Docker images
4. Restart containers
5. Verify API health

### One-time migration run (initial setup only)

The deploy workflow does NOT run migrations — the runtime containers don't have the .NET SDK, so `dotnet run` won't work inside them. Run migrations once during initial VM setup:

```bash
cd ~/signalstack

# SQL Server migrations
docker run --rm \
  --network signalstack-prod_default \
  -v $(pwd)/repo:/src \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run --project packages/migrations/SignalStack.SqlMigrations/SignalStack.SqlMigrations.csproj

# MongoDB migrations
docker run --rm \
  --network signalstack-prod_default \
  -v $(pwd)/repo:/src \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run --project packages/migrations/SignalStack.Migrations/SignalStack.Migrations.csproj

# Run seed
docker run --rm \
  --network signalstack-prod_default \
  -v $(pwd)/repo:/src \
  -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run --project apps/seed/SignalStack.Seed/SignalStack.Seed.csproj
```

**GitHub Secrets needed for CI/CD:**

| Secret | Value |
|--------|-------|
| `VM_HOST` | Your Oracle VM public IP |
| `VM_SSH_KEY` | Your private SSH key |
| `API_HOSTNAME` (variable) | `api.yourdomain.com` |

**Web app deployment** is automatic via Vercel — just connect your repo.

---

## Step 8: First Launch Checklist

### ⚠ Prerequisites — Code Changes

Before you start provisioning the VM, verify these in your repo:

- [x] **Dockerfiles created** — `apps/api/Dockerfile` and `apps/worker/Dockerfile` already exist in the repo with the bootstrap `COPY` lines (see Appendix A)
- [x] **Bootstrap files exist** — Both API and Worker have `bootstrap/appconfig.local.json`, `bootstrap/keyvault.local.json`, and `bootstrap/appconfig.lkg.json` in the repo

### Before first deploy:

- [ ] Oracle VM created and SSH accessible
- [ ] Docker and Caddy installed on VM
- [ ] Domain DNS pointing to VM IP
- [ ] Caddy reverse proxy configured and HTTPS working
- [ ] Google OAuth created with redirect URI `https://api.yourdomain.com/api/v1/auth/google/callback`
- [ ] FYERS app updated with redirect URI `https://api.yourdomain.com/api/v1/auth/fyers/callback`
- [ ] `~/signalstack/.env` populated with all secrets on the VM
- [ ] `~/signalstack/otel-config.yaml` created with your New Relic license key
- [ ] Vercel project created and builds successfully
- [ ] New Relic free account set up (otlp.eu01.nr-data.net)

### First startup sequence (important):

1. [ ] `docker compose pull` — download all base images
2. [ ] `docker compose build api worker` — build API + Worker with bootstrap files baked in
3. [ ] `docker compose up -d otel-collector` — start OTLP collector first
4. [ ] `docker compose up -d mongo redis mssql` — start databases
5. [ ] `docker compose up -d api worker` — finally start the apps
6. [ ] `docker compose ps` — verify all 7 containers are running
7. [ ] `docker compose logs api --tail 30` — no crash errors
8. [ ] `docker compose logs worker --tail 30` — should show "acquired singleton lease"

### Health checks:

```bash
# On the VM — verify all containers are running
docker ps

# Verify API responds
curl -s https://api.yourdomain.com/health
# Expected: HTTP 200

# Verify Worker logs
docker compose logs worker --tail 30
# Expected: Worker service started, Redis lease acquired

# Check observability
# Visit New Relic → APM → Services → find SignalStack.Api / SignalStack.Worker
```

---

## Appendix A: Dockerfiles

> **These Dockerfiles already exist in the repo** at `apps/api/Dockerfile` and `apps/worker/Dockerfile`.  The full contents are shown below for reference.

**The Dockerfiles MUST include the `bootstrap/` directory.** On startup, the code calls `AddSignalStackBootstrapConfiguration()` which loads `bootstrap/appconfig.local.json` and `bootstrap/keyvault.local.json`. If either file is missing, the app crashes with a `FileNotFoundException`.

These files exist in your repo at `apps/api/bootstrap/` and `apps/worker/bootstrap/` — they're already set up with localhost defaults.

**`apps/api/Dockerfile`:**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SignalStack.sln .
COPY Directory.Build.props .
COPY NuGet.Config .
COPY apps/api/ apps/api/
COPY packages/ packages/
RUN dotnet restore apps/api/SignalStack.Api.csproj
RUN dotnet publish apps/api/SignalStack.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# ⚠ CRITICAL: Bootstrap config files MUST be present at startup
# The AddSignalStackBootstrapConfiguration() method throws FileNotFoundException
# if bootstrap/appconfig.local.json is missing.  Copy the whole directory.
COPY apps/api/bootstrap/ bootstrap/

EXPOSE 8080
ENTRYPOINT ["dotnet", "SignalStack.Api.dll"]
```

**`apps/worker/Dockerfile`:**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SignalStack.sln .
COPY Directory.Build.props .
COPY NuGet.Config .
COPY apps/worker/ apps/worker/
COPY apps/api/ apps/api/
COPY packages/ packages/
RUN dotnet restore apps/worker/SignalStack.Worker.csproj
RUN dotnet publish apps/worker/SignalStack.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# ⚠ CRITICAL: Same bootstrap config requirement as API
COPY apps/worker/bootstrap/ bootstrap/

ENTRYPOINT ["dotnet", "SignalStack.Worker.dll"]
```

---

## Appendix B: Emergency Recovery

**VM reboot after power outage:**

```bash
ssh -i ~/.ssh/oracle_key ubuntu@<IP>
cd ~/signalstack
docker compose up -d
```

**Redeploy from scratch (VM re-created):**

```bash
# Install Docker + Caddy (Step 2.2)
# Clone repo
git clone https://github.com/YOUR_USER/signalstack.git ~/signalstack/repo

# Copy .env and docker-compose.yml
cd ~/signalstack
docker compose up -d
```

**Rollback a bad deploy:**

```bash
cd ~/signalstack/repo
git revert HEAD
git push origin main
# CI/CD will re-deploy previous version
```

---

## Summary: $28/Month Azure vs $0/Month Oracle+Vercel

| Area | Azure (B1 Plan) | Oracle VM + Vercel |
|------|----------------|-------------------|
| **Monthly cost** | ~$28-33 | **~$0.80** |
| **Setup time** | 1-2 hours | 2-3 hours |
| **Managed databases** | Yes | No (cron backups) |
| **Next.js hosting** | Shared B1 | **Vercel global CDN** (better!) |
| **Worker uptime** | Always-on (B1) | Always-on (VM) |
| **Backups** | Automatic | Cron + 14-day retention |
| **OS patching** | Azure handles | You handle `apt update` |
| **Scalability** | Click to upgrade | Redeploy to bigger VM |
| **Migration path** | None needed | ~1 day to move to Azure later |

**Bottom line:** For a solo POC that hasn't proved its worth, the Oracle + Vercel approach saves you ~$340/year with ~1 extra hour of setup effort. When you're ready for production, migrate to Azure managed services — the architecture is cloud-agnostic by design.
