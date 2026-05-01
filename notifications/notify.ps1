<#
.SYNOPSIS
  Plays a notification sound to signal task completion.

.DESCRIPTION
  Uses Windows-native .NET APIs — no dependencies required.
  Plays the system asterisk sound by default, or a custom .wav file.

.PARAMETER CustomWav
  Path to a custom .wav file to play instead of the system sound.

.PARAMETER Loop
  Repeat the sound N times (useful for urgent notifications).

.EXAMPLE
  .\notifications\notify.ps1
  .\notifications\notify.ps1 -CustomWav "C:\poc\notifications\chime.wav"
  .\notifications\notify.ps1 -Loop 3
#>

param(
  [string]$CustomWav = "",
  [int]$Loop = 1
)

Add-Type -AssemblyName System.Windows.Forms

for ($i = 0; $i -lt $Loop; $i++) {
  if ($CustomWav -ne "" -and (Test-Path $CustomWav)) {
    $player = New-Object System.Media.SoundPlayer($CustomWav)
    $player.PlaySync()
  } else {
    [System.Media.SystemSounds]::Asterisk.Play()
  }

  if ($Loop -gt 1 -and $i -lt ($Loop - 1)) {
    Start-Sleep -Milliseconds 300
  }
}
