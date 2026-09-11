# Creates the desktop shortcut for DSH.
# Usage:
#   powershell -ExecutionPolicy Bypass -File D:\DSH\make-shortcut.ps1
param(
    [string]$ShortcutPath = '',
    [string]$Target        = '',
    [string]$WorkDir       = ''
)

$ErrorActionPreference = 'Stop'

# Defaults resolve relative to THIS script's folder, so the files can be
# copied to any directory on any machine and the shortcut still points
# at the right launcher.
if (-not $ShortcutPath) { $ShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DSH.lnk' }
# Prefer the compiled DSH.exe (no cmd/powershell at run time) when present,
# otherwise fall back to start-dsh.cmd.
if (-not $Target) {
    if (Test-Path (Join-Path $PSScriptRoot 'DSH.exe')) {
        $Target = Join-Path $PSScriptRoot 'DSH.exe'
    } else {
        $Target = Join-Path $PSScriptRoot 'start-dsh.cmd'
    }
}
if (-not $WorkDir)       { $WorkDir       = $PSScriptRoot }

$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($ShortcutPath)
$sc.TargetPath       = $Target
$sc.WorkingDirectory = $WorkDir
$sc.WindowStyle      = 7            # 7 = minimized
$sc.Description      = 'Start DeepSeek Harness (DSH) web UI'
# Prefer app.ico (fallback DSH.ico) next to the launcher so the shortcut
# shows the custom icon.
$ico = Join-Path $PSScriptRoot 'app.ico'
if (-not (Test-Path $ico)) { $ico = Join-Path $PSScriptRoot 'DSH.ico' }
if (Test-Path $ico) {
    $sc.IconLocation = $ico
} else {
    $sc.IconLocation = "$env:SystemRoot\System32\shell32.dll,220"
}
$sc.Save()

Write-Output "Shortcut created: $ShortcutPath"
Write-Output "  -> Target : $Target"
Write-Output "  -> WorkDir: $WorkDir"
