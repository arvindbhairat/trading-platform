<#
.SYNOPSIS
  Watches execution_plan/status.json for task completions and plays a sound.

.DESCRIPTION
  Polls status.json every 5 seconds. When last_updated changes (agent just
  completed a task), it plays the Windows notification sound and shows a
  toast popup so you know without staring at the screen.

  Run this BEFORE starting an agent session. Press Ctrl+C to stop.

.NOTES
  Zero dependencies — uses only Windows-native .NET APIs.
#>

clear-host

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$statusPath = Join-Path $PSScriptRoot "..\execution_plan\status.json"
$statusPath = (Resolve-Path $statusPath).Path

if (-not (Test-Path $statusPath)) {
  Write-Host "ERROR: status.json not found at $statusPath" -ForegroundColor Red
  exit 1
}

Write-Host ""
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "   Task Completion Watcher (Ctrl+C to stop)" -ForegroundColor Cyan
Write-Host "  ========================================" -ForegroundColor Cyan
Write-Host "  Watching: $statusPath" -ForegroundColor DarkGray
Write-Host ""

# Read baseline (raw content for change detection)
$lastContent = Get-Content $statusPath -Raw
$json = $lastContent | ConvertFrom-Json
$lastTask = $json.completed_tasks[-1]

Write-Host "  Latest completed: $lastTask" -ForegroundColor DarkGray
Write-Host "  Waiting for file changes..." -ForegroundColor DarkGray
Write-Host "  Polling every 3 seconds..." -ForegroundColor DarkGray
Write-Host ""

$notifyScript = Join-Path $PSScriptRoot "notify.ps1"

while ($true) {
  Start-Sleep -Seconds 3

  try {
    $newContent = Get-Content $statusPath -Raw -ErrorAction Stop

    # Compare raw content — catches ANY change, not just last_updated
    if ($newContent -ne $lastContent) {
      $lastContent = $newContent
      $json = $newContent | ConvertFrom-Json
      $newTask = $json.completed_tasks[-1]

      $time = Get-Date -Format "HH:mm:ss"
      Write-Host "  $time CHANGE DETECTED in status.json!" -ForegroundColor Green
      Write-Host "  $time Latest completed task: $newTask" -ForegroundColor Green
      Write-Host "  $time Phase: $($json.current_phase) / $($json.current_version)" -ForegroundColor Green
      Write-Host "  $time >> Attempting notification sound via notify.ps1..." -ForegroundColor Yellow

      # Call notify.ps1 to play the sound
      & $notifyScript

      Write-Host "  $time >> notify.ps1 returned." -ForegroundColor Yellow

      # Show Windows toast notification
      $notify = New-Object System.Windows.Forms.NotifyIcon
      $notify.Icon = [System.Drawing.SystemIcons]::Information
      $notify.Visible = $true
      $notify.ShowBalloonTip(5000, "Task Complete", "Agent finished: $newTask - $($json.current_phase) / $($json.current_version)", [System.Windows.Forms.ToolTipIcon]::Info)
      $notify.Dispose()

      Write-Host "  $time >> Toast notification sent." -ForegroundColor Yellow
    }
  } catch {
    Write-Host "  $(Get-Date -Format 'HH:mm:ss') [WARN] Poll cycle error (file may be locked): $_" -ForegroundColor DarkGray
  }
}
