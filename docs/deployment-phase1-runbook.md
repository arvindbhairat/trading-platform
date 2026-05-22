# Phase 1 Deployment Runbook — Azure Portal Guide

## Overview

This runbook walks you through deploying **SignalStack Phase 1 (Solo Testing)** using only the Azure Portal, MongoDB Atlas, and a few third-party consoles. No Azure CLI required.

**Target:** ~$28-33/month, single Azure region (Central India), single user (you).

**Estimated time:** 2-3 hours spread across:

- Azure Portal provisioning: ~1 hour
- Third-party setup (Google OAuth, FYERS redirects): ~30 min
- GitHub Actions + first deploy: ~30 min
- Post-deployment verification: ~30 min

---

## Prerequisites

Before starting, make sure you have:

| Item | Status |
|------|--------|
| Active Azure subscription | ✅ You have this |
| FYERS developer account with an API app | ✅ You have this |
| Telegram bot token from BotFather | ✅ You have this |
| Google Cloud account (for OAuth) | ☐ Set up in Step 14 |
| GitHub repository with your SignalStack code pushed | ☐ Set up in Step 16 |

---

## Step 1: Create Resource Group

**Where:** Azure Portal → Resource groups

1. Click **+ Create**
2. Subscription: *(your subscription)*
3. Resource group: `rg-signalstack-solo`
4. Region: **Central India**
5. Click **Review + Create → Create**

---

## Step 2: Create Key Vault

**Where:** Azure Portal → Key Vaults

1. Click **+ Create**
2. Subscription & Resource group: select `rg-signalstack-solo`
3. Key vault name: `kv-signalstack-solo`
4. Region: **Central India**
5. Pricing tier: **Standard**
6. Go to **Access configuration** tab:
   - Permission model: **Vault access policy** (simpler for Phase 1)
   - Leave "Enable purge protection" checked
   - Leave "Soft delete" enabled (90-day retention)
7. Click **Review + Create → Create**

**After creation:**

8. Go to the Key Vault → **Access policies**
9. Click **+ Create**
10. Select **Key Vault Secrets Officer** (or select permissions: Get, List, Set, Delete on Secrets)
11. In **Principal**, search for and select **your own Azure AD account** (the one you're logged in with)
12. Click **Review + Create**
13. Repeat steps 9-12 to also give yourself **Key Vault Secrets User** permissions
14. In **Access policies** tab, also enable **Azure Virtual Machines for deployment** (not needed now but good to have)

> ⚠ Save this URI for later: `https://kv-signalstack-solo.vault.azure.net/`

---

## Step 3: Create App Configuration

**Where:** Azure Portal → App Configuration

1. Click **+ Create**
2. Resource group: `rg-signalstack-solo`
3. Location: **Central India**
4. Name: `appcs-signalstack-solo`
5. Pricing tier: **Free**
6. Click **Review + Create → Create**

**After creation — add keys:**

7. Go to the resource → **Configuration explorer** → **+ Create** → **Key-value**
8. Add the following:

| Key | Value | Label |
|-----|-------|-------|
| `Telemetry:Otlp:PrimaryEndpoint` | `http://localhost:4317` | *(leave blank)* |
| `Telemetry:Otlp:UseFallbackEndpoint` | `false` | *(leave blank)* |
| `Logging:LogLevel:Default` | `Information` | *(leave blank)* |
| `Logging:LogLevel:SignalStack` | `Debug` | *(leave blank)* |

9. Click **Apply** for each

> ⚠ Save this endpoint for later: `https://appcs-signalstack-solo.azconfig.io`

---

## Step 4: Create App Service Plan

**Where:** Azure Portal → App Service plans

1. Click **+ Create**
2. Resource group: `rg-signalstack-solo`
3. Name: `asp-signalstack-solo`
4. Operating System: **Linux**
5. Region: **Central India**
6. Pricing plan: **Basic B1** (~$13/month — always-on, essential for the Worker)
   - *Don't pick F1 (Free) — it sleeps after 20 minutes, which kills the Worker*
7. Click **Review + Create → Create**

> This single plan will host all 3 apps (API, Worker, Web).

---

## Step 5: Create SQL Server + Database

**Where:** Azure Portal → SQL databases

1. Click **+ Create** → **SQL Database**
2. Resource group: `rg-signalstack-solo`
3. Database name: `sqldb-signalstack-solo`
4. **Server:** Click **Create new**
   - Server name: `sql-signalstack-solo`
   - Location: **Central India**
   - Authentication method: **Use SQL Authentication** (simpler for Phase 1)
   - Server admin login: `sqladmin`
   - Password: *(create a strong password and save it somewhere safe)*
   - Click **OK**
5. *Do you want to use SQL elastic pool?* **No**
6. Workload environment: **Development**
7. Compute + storage: Click **Configure database**
   - Service tier: **General Purpose — Serverless** (it pauses when idle, saving $)
   - vCores: **1 vCore**
   - Max vCores: **1**
   - Auto-pause delay: **60 minutes** (pauses after 1hr idle)
   - Click **Apply**
8. Backup storage redundancy: **Locally-redundant backup storage**
9. Click **Review + Create → Create**

**After creation — get connection string:**

10. Go to the SQL server resource → **Firewalls and virtual networks**
11. Click **+ Add client IPv4 address** (your IP) and **Save**
12. Also check **Allow Azure services and resources to access this server** → **Yes**
13. Go to the database (`sqldb-signalstack-solo`) → **Connection strings**
14. Copy the **ADO.NET** connection string
15. Replace `{your_password}` with your actual password
16. Save this for the Key Vault later

> ⚠ The connection string looks like:
> `Server=tcp:sql-signalstack-solo.database.windows.net,1433;Initial Catalog=sqldb-signalstack-solo;Persist Security Info=False;User ID=sqladmin;Password={YOUR_PASSWORD};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;`

---

## Step 6: Create Redis Cache

**Where:** Azure Portal → Azure Cache for Redis

1. Click **+ Create**
2. Resource group: `rg-signalstack-solo`
3. DNS name: `redis-signalstack-solo`
4. Location: **Central India**
5. Cache type: **Standard C0 (250 MB)** — ~$15/month
6. Click **Review + Create → Create**

**After creation — get connection string:**

7. Go to the resource → **Settings** → **Access keys**
8. Copy the **Primary connection string (StackExchange.Redis)**
9. Save this for Key Vault later

> ⚠ The connection string looks like:
> `redis-signalstack-solo.redis.cache.windows.net:6380,password={YOUR_KEY},ssl=True,abortConnect=False`

---

## Step 7: Create MongoDB Atlas M0 Cluster

**Where:** [MongoDB Atlas](https://www.mongodb.com/cloud/atlas/register)

1. Sign up or log in to MongoDB Atlas
2. Click **+ Create** (or "Build Database")
3. Select **M0 FREE** (the free tier)
4. Provider: **Azure**
5. Region: **Central India** (look for "Central India (Central India)" — might show as `azure / centralindia`)
6. Cluster name: `SignalStack-Solo`
7. Click **Create Cluster** (takes 1-3 minutes)

**After creation — create database user & get connection string:**

8. Go to **Security** → **Database Access**
9. Click **+ Add New Database User**
10. Username: `signalstack` (or your choice)
11. Password: *Create a strong password and save it*
12. Database User Privileges: **Atlas Admin** (for Phase 1)
13. Click **Add User**

14. Go to **Security** → **Network Access**
15. Click **+ Add IP Address**
16. Click **Allow Access from Anywhere** (0.0.0.0/0) — this is fine for Phase 1 solo testing because:
    - App Services connect from dynamic Azure IPs
    - Your dev machine connects from your IP
    - The secrets (connection string) are locked in Key Vault
    - **Phase 2:** restrict to specific Azure VNet
17. Click **Confirm**

18. Go to **Deployment** → **Database** → click **Connect**
19. Choose **Drivers**
20. Copy the connection string — it looks like:
    `mongodb+srv://signalstack:{PASSWORD}@signalstack-solo.xxxxx.mongodb.net/?retryWrites=true&w=majority`
21. Replace `{PASSWORD}` with your actual password
22. Save this connection string for Key Vault

---

## Step 8: Create API App Service

**Where:** Azure Portal → App Services

1. Click **+ Create** → **Web App**
2. Resource group: `rg-signalstack-solo`
3. Name: `app-signalstack-api-solo`
4. **Publish:** **Code**
5. **Runtime stack:** **.NET 10 (Linux)** *(if .NET 10 isn't listed, pick .NET 9 — the deployment plan targets .NET 10)*
6. **Operating System:** **Linux**
7. **Region:** **Central India**
8. **Linux Plan:** Select **asp-signalstack-solo** (already created)
9. Click **Review + Create → Create**

---

## Step 9: Create Worker App Service

**Where:** Azure Portal → App Services

1. Click **+ Create** → **Web App**
2. Resource group: `rg-signalstack-solo`
3. Name: `app-signalstack-worker-solo`
4. Publish: **Code**
5. Runtime stack: **.NET 10 (Linux)**
6. Operating System: **Linux**
7. Region: **Central India**
8. Linux Plan: **asp-signalstack-solo** (shared plan)
9. Click **Review + Create → Create**

**⚠ Critical Worker setting — must stay at 1 instance:**

After creation:
10. Go to the Worker App Service → **Settings** → **Scale up (App Service plan)**
11. Verify the plan shows **B1 (1 instance)**
12. Go to **Scale out** → ensure **Manual scale** is selected and **Instances = 1**
13. Do **NOT** set any auto-scale rules

---

## Step 10: Create Web App Service

**Where:** Azure Portal → App Services

1. Click **+ Create** → **Web App**
2. Resource group: `rg-signalstack-solo`
3. Name: `app-signalstack-web-solo`
4. Publish: **Code**
5. Runtime stack: **Node.js 24 (Linux)**
6. Operating System: **Linux**
7. Region: **Central India**
8. Linux Plan: **asp-signalstack-solo** (shared plan)
9. Click **Review + Create → Create**

---

## Step 11: Enable Managed Identities + Key Vault Access

### 11.1 Enable system-assigned managed identity on each App Service

For **each** of the 3 App Services (API, Worker, Web):

1. Go to the App Service → **Settings** → **Identity**
2. Under **System assigned**, set **Status** to **On**
3. Click **Save** → **Yes**
4. Copy the **Object (principal) ID** for each app — you'll need these

### 11.2 Grant Key Vault access

Go to **Key Vault** → **Access policies** → **+ Create**

**For the API App Service:**
1. Select permissions: Check **Get**, **List**, **Set**, **Delete** for Secrets (the API needs read AND write for Telegram tokens and signed-payload keys)
2. Principal: Search for `app-signalstack-api-solo`
3. Click **Review + Create**

**For the Worker App Service:**
1. Select permissions: Check **Get**, **List** for Secrets (read-only)
2. Principal: Search for `app-signalstack-worker-solo`
3. Click **Review + Create**

**For the Web App Service:**
1. Select permissions: Check **Get**, **List** for Secrets (read-only)
2. Principal: Search for `app-signalstack-web-solo`
3. Click **Review + Create**

---

## Step 12: Configure App Service Settings

### 12.1 API App Service settings

Go to `app-signalstack-api-solo` → **Settings** → **Environment variables**

Add these (click **+ Add** for each):

| Name | Value |
|------|-------|
| `KEY_VAULT_ENDPOINT` | `https://kv-signalstack-solo.vault.azure.net/` |
| `APP_CONFIG_ENDPOINT` | `https://appcs-signalstack-solo.azconfig.io` |
| `SEED_ADMIN_EMAIL` | `your-email@example.com` **(use YOUR actual email)** |
| `ENVIRONMENT` | `Development` |
| `ASPNETCORE_ENVIRONMENT` | `Development` |

**FYERS redirect URIs — add these to the API app settings as well (the app needs to know its own URL):**

| Name | Value |
|------|-------|
| `WEBSITE_HOSTNAME` | `app-signalstack-api-solo.azurewebsites.net` |

Click **Save**.

### 12.2 Worker App Service settings

Go to `app-signalstack-worker-solo` → **Settings** → **Environment variables**

| Name | Value |
|------|-------|
| `KEY_VAULT_ENDPOINT` | `https://kv-signalstack-solo.vault.azure.net/` |
| `APP_CONFIG_ENDPOINT` | `https://appcs-signalstack-solo.azconfig.io` |
| `SEED_ADMIN_EMAIL` | `your-email@example.com` |
| `ENVIRONMENT` | `Development` |
| `ASPNETCORE_ENVIRONMENT` | `Development` |

Click **Save**.

### 12.3 Web App Service settings

Go to `app-signalstack-web-solo` → **Settings** → **Environment variables**

| Name | Value |
|------|-------|
| `NEXT_PUBLIC_API_BASE_URL` | `https://app-signalstack-api-solo.azurewebsites.net` |
| `KEY_VAULT_ENDPOINT` | `https://kv-signalstack-solo.vault.azure.net/` |
| `APP_CONFIG_ENDPOINT` | `https://appcs-signalstack-solo.azconfig.io` |
| `ENVIRONMENT` | `Development` |

Click **Save**.

---

## Step 13: Configure CORS on API

Go to `app-signalstack-api-solo` → **Settings** → **CORS**

1. **Allowed Origins:** Add `https://app-signalstack-web-solo.azurewebsites.net`
2. **Allowed Methods:** Select **GET, POST, PUT, DELETE, OPTIONS**
3. **Allowed Headers:** `*`
4. **Credentials:** Check **Enable Access-Control-Allow-Credentials**
5. Click **Save**

---

## Step 14: Third-Party Integrations

### 14.1 Google OAuth 2.0

**Where:** [Google Cloud Console](https://console.cloud.google.com/)

1. Go to **APIs & Services** → **Credentials**
2. Click **+ Create Credentials** → **OAuth 2.0 Client ID**
3. Application type: **Web Application**
4. Name: `SignalStack Portal`
5. **Authorized JavaScript origins:**
   - `https://app-signalstack-web-solo.azurewebsites.net`
   - `http://localhost:3000` (for local development)
6. **Authorized redirect URIs:**
   - `https://app-signalstack-api-solo.azurewebsites.net/api/v1/auth/google/callback`
   - `http://localhost:3000/api/v1/auth/google/callback` (for local dev)
7. Click **Create**
8. Copy the **Client ID** and **Client Secret** shown in the dialog

### 14.2 FYERS API App — Update Redirect URI

**Where:** [FYERS Developer Dashboard](https://developer.fyers.in/)

1. Log in, go to your API application
2. Add this redirect URI:
   - `https://app-signalstack-api-solo.azurewebsites.net/api/v1/auth/fyers/callback`
3. Note your **APP ID** and **SECRET KEY** (you should have these already)

### 14.3 Telegram Bot (already created)

You should already have your bot token from BotFather. If not:
1. Open Telegram, search `@BotFather`
2. Send `/newbot` and follow prompts
3. Save the token (`1234567890:ABCdefGHIjklmNOPqrstUVwxyz`)

---

## Step 15: Populate Key Vault Secrets

**Where:** Azure Portal → `kv-signalstack-solo` → **Objects** → **Secrets**

Click **+ Generate/Import** for each:

| Secret Name | Value |
|-------------|-------|
| `fyers--app-id` | Your FYERS APP ID |
| `fyers--secret-key` | Your FYERS APP SECRET |
| `oauth--google--client-id` | Google OAuth Client ID (from Step 14.1) |
| `oauth--google--client-secret` | Google OAuth Client Secret |
| `telegram--bot-token` | Your Telegram bot token |
| `mongodb--connection-string` | MongoDB Atlas connection string (from Step 7) |
| `sqlserver--connection-string` | SQL Server connection string (from Step 5) |
| `redis--connection-string` | Redis connection string (from Step 6) |
| `session-signing-key` | Run this to generate: *(see below)* |
| `orders-intent-hmac-key` | Run this to generate: *(see below)* |

**Generating `session-signing-key` and `orders-intent-hmac-key`:**

Since you can't run CLI commands, use an online tool like [https://generate-random.org/encryption-key-generator](https://generate-random.org/encryption-key-generator) or use the **Azure Portal Cloud Shell** (top bar):

1. Click the **>_** icon in the Azure Portal top bar → **Bash**
2. Run:

```bash
# Generate a 256-bit (32-byte) random key in Base64 for session signing
openssl rand -base64 32

# Generate another 256-bit key for order HMAC
openssl rand -base64 32
```

3. Copy each output into its respective secret in Key Vault

---

## Step 16: GitHub Actions Setup

### 16.1 Push your code to GitHub

If your code isn't on GitHub yet:

1. Create a new repository on GitHub (e.g., `signalstack`)
2. From your local machine:
```bash
git remote add origin https://github.com/{YOUR_USERNAME}/signalstack.git
git push -u origin main
```

### 16.2 Create the deploy workflow

**Where:** GitHub.com → Your repo → **Actions**

The deployment plan includes a complete `deploy-all.yml` workflow (Section 8.1). You need to create this file in your repo at `.github/workflows/deploy-all.yml`.

**How to create it through GitHub.com:**

1. Go to your GitHub repo
2. Click **Add file** → **Create new file**
3. Path: `.github/workflows/deploy-all.yml`
4. Paste the YAML from Appendix A of this document (scroll to bottom)
5. Commit directly to `main`

### 16.3 Add GitHub Actions secrets

**Where:** GitHub.com → Your repo → **Settings** → **Secrets and variables** → **Actions**

**Secrets** (click **New repository secret**):

| Name | Value |
|------|-------|
| `AZURE_WEBAPP_PUBLISH_PROFILE_API` | Publish profile from API App Service *(see below)* |
| `AZURE_WEBAPP_PUBLISH_PROFILE_WORKER` | Publish profile from Worker App Service |
| `AZURE_WEBAPP_PUBLISH_PROFILE_WEB` | Publish profile from Web App Service |
| `SQL_CONNECTION_STRING` | SQL Server connection string |
| `MONGO_CONNECTION_STRING` | MongoDB Atlas connection string |

**How to get publish profiles:**

1. Go to each App Service in Azure Portal
2. **Overview** → **Download publish profile** (Get Publish Profile button)
3. Open the downloaded `.PublishSettings` file with a text editor
4. Copy the **entire contents** and paste it into the GitHub secret

**Variables** (click **New repository variable**):

| Name | Value |
|------|-------|
| `API_APP_NAME` | `app-signalstack-api-solo` |
| `WORKER_APP_NAME` | `app-signalstack-worker-solo` |
| `WEB_APP_NAME` | `app-signalstack-web-solo` |
| `SEED_ADMIN_EMAIL` | `your-email@example.com` |
| `SEED_DISCLAIMER_SHORT` | `Trading involves risk. Not investment advice.` |
| `SEED_DISCLAIMER_URL` | `https://app-signalstack-web-solo.azurewebsites.net/legal/disclaimer` |

---

## Step 17: First Deployment

### Option A: Via GitHub Actions (if you set up the workflow)

1. Push a change to `main` branch — or trigger the workflow manually:
2. Go to GitHub repo → **Actions** → **Build and Deploy (Phase 1)** → **Run workflow**
3. Monitor the workflow run

### Option B: Manual deploy via Azure Portal (alternative)

If you prefer not to set up CI/CD yet, deploy directly:

**For API and Worker (.NET apps):**

1. On your dev machine, build the project:
```bash
dotnet publish apps/api/SignalStack.Api.csproj -c Release -o ./publish/api
dotnet publish apps/worker/SignalStack.Worker.csproj -c Release -o ./publish/worker
```

2. Zip the publish folders:
```bash
# Windows: Right-click → Send to Compressed folder
# Or: Compress-Archive -Path ./publish/api/* -DestinationPath api.zip
```

3. In Azure Portal, go to each App Service → **Deployment** → **Deployment Center**
4. Upload the zip file and deploy

**For Web (Node.js):**

1. On your dev machine:
```bash
cd apps/web
npm ci
npm run build
```

2. Zip the entire `apps/web` folder (including `.next`, `node_modules`, `public`, `src`)
3. Upload via Deployment Center

---

## Step 18: Post-Deployment Verification

Tick these off after the first successful deployment:

### Health checks

- [ ] Visit `https://app-signalstack-api-solo.azurewebsites.net/health` → should return **200 OK**
- [ ] Visit `https://app-signalstack-web-solo.azurewebsites.net` → should load the login page
- [ ] Visit `https://app-signalstack-worker-solo.azurewebsites.net` → should return **200 OK** (Worker health endpoint)

### Database connectivity

- [ ] API can connect to MongoDB and SQL Server (check App Service Log Stream for no connection errors)
- [ ] Worker started successfully (check Log Stream for startup messages)

### Authentication

- [ ] Google OAuth sign-in flow works — click "Sign in with Google"
- [ ] Your email is auto-elevated to admin (matches `SEED_ADMIN_EMAIL`)
- [ ] You can access admin dashboard

### FYERS

- [ ] FYERS token management page loads in admin portal
- [ ] You can generate a daily FYERS token

### Logging

- [ ] No unexpected 500 errors in App Service Log Stream
- [ ] OTLP telemetry visible (if you set up Application Insights later)

---

## Monthly Cost Breakdown

| Service | SKU | Cost |
|---------|-----|------|
| App Service Plan (shared B1) | Linux B1 | ~$13 |
| SQL Database | Serverless GP 1 vCore | ~$5-15 |
| Redis Cache | Standard C0 | ~$15 |
| Key Vault | Standard | ~$0 |
| App Configuration | Free | $0 |
| MongoDB Atlas | M0 Free | $0 |
| **Total** | | **~$28-33/month** |

---

## Appendix A: GitHub Actions Workflow

Create `.github/workflows/deploy-all.yml` in your repo with this content:

```yaml
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
      - name: SQL Server migrations
        run: dotnet run --project packages/migrations/SignalStack.SqlMigrations/SignalStack.SqlMigrations.csproj
        env:
          ConnectionStrings__SqlServer: ${{ secrets.SQL_CONNECTION_STRING }}

      - name: MongoDB migrations
        run: dotnet run --project packages/migrations/SignalStack.Migrations/SignalStack.Migrations.csproj
        env:
          ConnectionStrings__MongoDb: ${{ secrets.MONGO_CONNECTION_STRING }}

      - name: sys_config seeding
        run: dotnet run --project apps/seed/SignalStack.Seed/SignalStack.Seed.csproj
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

---

## Appendix B: Quick Reference — All Resource Names

| Resource | Name |
|----------|------|
| Resource Group | `rg-signalstack-solo` |
| Key Vault | `kv-signalstack-solo` |
| App Configuration | `appcs-signalstack-solo` |
| App Service Plan (shared) | `asp-signalstack-solo` |
| API App Service | `app-signalstack-api-solo` |
| Worker App Service | `app-signalstack-worker-solo` |
| Web App Service | `app-signalstack-web-solo` |
| SQL Server | `sql-signalstack-solo` |
| SQL Database | `sqldb-signalstack-solo` |
| Redis Cache | `redis-signalstack-solo` |
| MongoDB Atlas Cluster | `SignalStack-Solo` |
