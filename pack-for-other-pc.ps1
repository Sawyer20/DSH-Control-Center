# ============================================================================
#  pack-for-other-pc.ps1 - build a clean, self-contained runtime package
#
#  Whitelist-based on purpose: only what the target PC needs to install and run.
#  Engineering records (docs/, AGENTS.md), dev tools (probes), build artifacts
#  (DSH-old.exe, *.bak-*) and machine state (node_modules, .tools, logs) are
#  NEVER shipped.
#
#  Also copies the Node installer from the workspace root (..\node-v*-x64.msi)
#  into redist\, so the target machine can install Node OFFLINE: setup.ps1
#  unpacks it with `msiexec /a` (administrative install, no admin rights).
#
#  Default output: <same drive>\DSH-Publish\DSH-App   (override: -OutDir "X:\path")
# ============================================================================

param([string]$OutDir = '')

$ErrorActionPreference = 'Stop'

function Remove-Tree([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    try {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        return
    } catch { }
    try {
        cmd.exe /c "rd /s /q `"$path`"" | Out-Null
    } catch { }
}

$src = Split-Path -Parent $MyInvocation.MyCommand.Definition   # this DSH-App folder
if (-not (Test-Path (Join-Path $src 'DSH.cs'))) {
    throw 'DSH.cs not found - run this script from inside the DSH-App folder.'
}

if ($OutDir) {
    $out = $OutDir
} else {
    $drive = Split-Path -Qualifier $src
    $out   = Join-Path $drive 'DSH-Publish'
}
$dst = Join-Path $out 'DSH-App'

# clean previous publish output
Remove-Tree $out
New-Item -ItemType Directory -Path $dst | Out-Null

# --- whitelist: sources, build/install scripts, assets, deps, docs, artifact --
$packFiles = @(
    # sources (target machine rebuilds the exe locally)
    'DSH.cs', 'WpfUI.cs', 'TrayNative.cs', 'Win11Backdrop.cs', 'JobKill.cs',
    'Usage.cs', 'Diagnostics.cs', 'Sessions.cs', 'Backup.cs', 'Notifications.cs', 'Mux.cs', 'PetScene.cs', 'PetWindow.cs',
    # build + install + launch
    'build-dsh-exe.cmd', 'install-dsh.cmd', 'setup.ps1', 'start-dsh.cmd', 'make-shortcut.ps1',
    # manifest / icons / logo
    'app.manifest', 'app.ico', 'DSH.ico',
    'tray_running.ico', 'tray_starting.ico', 'tray_error.ico', 'tray_offline.ico',
    'whale_base.png',
    # dependencies
    'package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml', '.npmrc',
    # user-facing doc + prebuilt exe
    'README.md', 'DSH.exe'
)
$required = @(
    'DSH.cs', 'WpfUI.cs', 'TrayNative.cs', 'Win11Backdrop.cs', 'JobKill.cs',
    'Usage.cs', 'Diagnostics.cs', 'Sessions.cs', 'Backup.cs', 'Notifications.cs', 'Mux.cs', 'PetScene.cs', 'PetWindow.cs',
    'build-dsh-exe.cmd', 'install-dsh.cmd', 'setup.ps1',
    'app.manifest', 'package.json', 'README.md', 'DSH.exe'
)

Write-Host 'Packing to:' $dst
Write-Host '  whitelist: app runtime only (no docs/AGENTS, no dev tools, no build artifacts)'

foreach ($f in $packFiles) {
    $from = Join-Path $src $f
    if (Test-Path -LiteralPath $from) {
        Copy-Item -LiteralPath $from -Destination (Join-Path $dst $f) -Force
    } else {
        Write-Host ('  ! missing, skipped: ' + $f)
    }
}

$missing = @()
foreach ($f in $required) { if (-not (Test-Path (Join-Path $dst $f))) { $missing += $f } }
if ($missing.Count -gt 0) {
    throw ('required files missing from the package: ' + ($missing -join ', '))
}

# --- offline Node installer (kept in the workspace root, next to DSH-App) -----
$msi = Get-ChildItem -LiteralPath (Join-Path $src '..') -Filter 'node-v*-x64.msi' -File -ErrorAction SilentlyContinue |
       Select-Object -First 1
if ($msi) {
    $redist = Join-Path $dst 'redist'
    New-Item -ItemType Directory -Path $redist | Out-Null
    Copy-Item -LiteralPath $msi.FullName -Destination (Join-Path $redist $msi.Name) -Force
    Write-Host ('  + offline Node installer: redist\' + $msi.Name)
} else {
    Write-Host '  ! node-v*-x64.msi not found next to DSH-App'
    Write-Host '    -> the target PC will download Node.js online instead'
}

$files = (Get-ChildItem -LiteralPath $dst -Recurse -File).Count
$sizeMb = [math]::Round(((Get-ChildItem -LiteralPath $dst -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)

Write-Host ''
Write-Host '============================================================'
Write-Host ('  Packed OK: ' + $dst + '  (' + $files + ' files, ' + $sizeMb + ' MB)')
Write-Host ''
Write-Host ('  1) Copy the folder "' + $out + '" to the other PC.')
Write-Host ('  2) First time there: open DSH-App and double-click install-dsh.cmd')
Write-Host ('     Node comes from redist\ (offline); pnpm deps still need internet once.')
Write-Host ('  3) Later updates there: copy the changed files over and double-click')
Write-Host ('     build-dsh-exe.cmd (no internet needed - uses the built-in csc).')
Write-Host ('  4) Double-click the desktop "DSH" to start.')
Write-Host '============================================================'
