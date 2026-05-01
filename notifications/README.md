# Task Completion Notifications

Plays a Windows sound when an AI agent finishes executing a task from `execution_plan/`.

## Quick Start

### Option A: Automatic watcher (recommended)

Open **PowerShell** and run:

```powershell
.\notifications\watch-completions.ps1
```

Leave it running in the background while agent sessions execute tasks.
When `execution_plan/status.json` is updated (task complete), you'll hear a sound automatically.

### Option B: Manual test

```powershell
.\notifications\notify.ps1
```

### Option C: Repeated (loud) notification

```powershell
.\notifications\notify.ps1 -Loop 3
```

## How it works

Both scripts use **only Windows-native .NET APIs** (`System.Media.SystemSounds` and `System.IO.FileSystemWatcher`).
No npm, Python, or third-party installs required.
