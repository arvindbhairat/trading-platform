# Dev Notes: Setting Up WSL for Claude Desktop Agent Workflows on Windows 11

**Date:** 2026-05-01  
**Purpose:** Resolve "The Linux build environment" error in Claude Desktop when using DeepSeek models via API, and enable agent workflows with passwordless sudo.

---

## 1. Enable Required Windows Features (PowerShell as Admin)

Run each command in an elevated PowerShell window. Restart your PC after executing all four.

```powershell
dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart
Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform -All
Enable-WindowsOptionalFeature -Online -FeatureName Containers -All
```

**Why:**
- **Microsoft-Windows-Subsystem-Linux** – provides the core WSL functionality.
- **VirtualMachinePlatform** – required for WSL 2.
- **HypervisorPlatform** – enables Hyper-V components needed for virtualization.
- **Containers** – adds container support used by advanced agent tools.

Without these, Claude Desktop cannot start its Linux build environment.

---

## 2. Install Ubuntu Distribution

After restarting, open a normal PowerShell window and run:

```powershell
wsl --install -d Ubuntu
```

During installation, create a Linux **username** and **password**. This user becomes the default WSL user.

**Why:** Claude Desktop needs a full Linux distribution. Ubuntu is the recommended option.

---

## 3. Allow Passwordless sudo (for Agent Automation)

Open the Ubuntu terminal and edit the sudoers file:

```bash
sudo visudo
```

Add this line at the end (replace `your_username`):

```text
your_username ALL=(ALL) NOPASSWD: ALL
```

Save and exit (nano: **Ctrl+X**, **Y**, **Enter**).

**Why:** Agent workflows run non-interactively. This prevents sudo password prompts from blocking automation.

**Security note:** Only enable this in trusted development environments.

---

## 4. Verify Passwordless sudo and WSL Integration

From PowerShell:

```powershell
wsl echo "Hello from agent"
```

Expected output:

```text
Hello from agent
```

Then test sudo:

```powershell
wsl sudo apt update
```

If no password is requested, the setup is correct.

---

## 5. Fully Update the Ubuntu Environment

Run this command from PowerShell:

```powershell
wsl bash -c "sudo apt update && sudo apt upgrade -y"
```

**Why:** Ensures the system is fully up to date with no manual prompts.

---

## ✅ Summary

After completing these steps, Claude Desktop will:
- Detect the WSL Ubuntu environment automatically.
- Run Linux build tasks without errors.
- Execute sudo commands without prompts.

---

## Maintenance Tip

Occasionally run:

```powershell
wsl bash -c "sudo apt update && sudo apt upgrade -y"
```

These notes document the exact procedure performed on 2026-05-01.

---

## 6. Local Database Configuration

The platform uses **MongoDB** and **PostgreSQL** for local development. Connection strings are configured via a `.env` file at the repo root.

### Prerequisites

- [MongoDB](https://www.mongodb.com/try/download/community) installed locally (default port 27017)
- [PostgreSQL 16](https://www.postgresql.org/download/) installed locally (default port 5432)

### Setup

1. Copy or edit the `.env` file at the repo root:

```bash
# From C:\poc\
notepad .env
```

Contents (already created):

```env
# Database connection strings for local development
# Auto-loaded by DotNetEnv package in API and Worker projects.
# No manual sourcing needed — just dotnet run.

export ConnectionStrings__MongoDb="mongodb://localhost:27017/signalstack"
export MongoDB__DatabaseName="signalstack"
export ConnectionStrings__SqlServer="Host=localhost;Port=5432;Database=signalstack;Username=postgres;Password=your_password_here;"
```

2. Create both databases (if they don't exist):

```bash
# Create PostgreSQL database
psql -h localhost -U postgres -c "CREATE DATABASE signalstack"

# MongoDB creates databases on first use — the application will create it automatically.
```

### How It Works

Both `apps/api` and `apps/worker` load the `.env` file at startup via the `DotNetEnv` NuGet package (wired in `Program.cs`). The environment variables are read by .NET's configuration system using these keys:

| Config Key | Env Var | Value |
|---|---|---|
| `ConnectionStrings:MongoDb` | `ConnectionStrings__MongoDb` | MongoDB connection string |
| `MongoDB:DatabaseName` | `MongoDB__DatabaseName` | MongoDB database name |
| `ConnectionStrings:SqlServer` | `ConnectionStrings__SqlServer` | PostgreSQL connection string (Npgsql format) |

The `.env` file is git-ignored (`*.env` rule in `.gitignore`) so secrets stay local.

### Running

```bash
# API
dotnet run --project apps/api

# Worker
dotnet run --project apps/worker
```

No manual sourcing or environment variable setup needed.
