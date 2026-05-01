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
