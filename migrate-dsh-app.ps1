# ============================================================================
#  migrate-dsh-app.ps1  - safe migration: D:\DSH  ->  D:\DSH\DSH-App
#
#  Runs on PowerShell 7 (pwsh) and Windows PowerShell 5.1; normally via
#  migrate-to-dsh-app.cmd (the cmd wrapper prefers pwsh, falls back to 5.1,
#  and keeps the window open).
#
#  Safety rules implemented:
#    * Aborts if DSH is still running (process or port 3080).
#    * Never moves node_modules (pnpm junctions would break).
#    * Relinks a fresh node_modules inside DSH-App and VERIFIES it before
#      the old D:\DSH\node_modules is removed - the old copy stays usable
#      if anything fails.
#    * Rebuilds DSH.exe and the desktop shortcut at the new location.
# ============================================================================

$ErrorActionPreference = 'Stop'

$Root = 'D:\DSH'
$App  = Join-Path $Root 'DSH-App'
$SelfNames = @('migrate-dsh-app.ps1', 'migrate-to-dsh-app.cmd')

function Say([string]$m)  { Write-Host $m -ForegroundColor Cyan }
function Warn([string]$m) { Write-Host $m -ForegroundColor Yellow }

function Test-DshRunning {
    if (Get-Process -Name 'DSH' -ErrorAction SilentlyContinue) { return $true }
    if (Get-NetTCPConnection -LocalPort 3080 -State Listen -ErrorAction SilentlyContinue) { return $true }
    return $false
}

try {
    if (-not (Test-Path (Join-Path $Root 'DSH.cs'))) {
        throw "DSH.cs not found at $Root - run this script from the software folder."
    }

    # ---- 0. prerequisites -------------------------------------------------
    if (Test-DshRunning) {
        throw "DSH is still running. Quit it first (tray icon -> Exit (stop service)), then rerun."
    }

    # ---- 1. prepare DSH-App ----------------------------------------------
    if (Test-Path $App) {
        if ((Get-ChildItem -LiteralPath $App -Force | Select-Object -First 1)) {
            throw "$App is not empty - move its contents away or delete it, then rerun."
        }
        Remove-Item -LiteralPath $App -Recurse -Force
    }
    New-Item -ItemType Directory -Path $App | Out-Null
    Say '[1/5] Moving software files into DSH-App ...'

    $items = Get-ChildItem -LiteralPath $Root -Force |
        Where-Object { $_.Name -ne 'DSH-App' -and $_.Name -ne 'node_modules' -and ($SelfNames -notcontains $_.Name) }
    foreach ($it in $items) {
        try {
            Move-Item -LiteralPath $it.FullName -Destination $App -ErrorAction Stop
        } catch {
            Warn ('    move skipped: ' + $it.Name + ' -> ' + $_.Exception.Message)
        }
    }

    # ---- 2. relink a fresh node_modules inside DSH-App --------------------
    Say '[2/5] Relinking dependencies inside DSH-App (fresh node_modules) ...'
    Push-Location $App
    try {
        $pnpmCjs = Join-Path $App '.tools\pnpm-home\node_modules\pnpm\bin\pnpm.cjs'
        if (Test-Path $pnpmCjs) {
            $proc = Start-Process -FilePath 'node' -ArgumentList @('"' + $pnpmCjs + '"', 'install', '--reporter', 'append-only') -WorkingDirectory $App -NoNewWindow -Wait -PassThru
        } else {
            $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', 'pnpm install --reporter append-only') -WorkingDirectory $App -NoNewWindow -Wait -PassThru
        }
        if ($proc.ExitCode -ne 0) {
            throw "pnpm install failed (exit code $($proc.ExitCode)). Your old copy at D:\DSH is still intact (node_modules was NOT deleted). To restore, move the contents of $App back to $Root."
        }
    } finally {
        Pop-Location
    }

    # ---- 3. verify the new install before touching the old one ------------
    $newBin = Join-Path $App 'node_modules\@deepseek-ai\dsh\lib\bin.js'
    if (-not (Test-Path $newBin)) {
        throw "Relink finished but $newBin is missing - investigate before cleaning anything."
    }
    Say '[3/5] New install verified: node_modules\@deepseek-ai\dsh\lib\bin.js'

    # ---- 4. rebuild DSH.exe + desktop shortcut ----------------------------
    Say '[4/5] Rebuilding DSH.exe (tray version) and the desktop shortcut ...'
    $buildExe = Join-Path $App 'build-dsh-exe.cmd'
    if (Test-Path $buildExe) {
        $b = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', '"' + $buildExe + '" < nul') -WorkingDirectory $App -NoNewWindow -Wait -PassThru
        if ($b.ExitCode -ne 0) { throw "build-dsh-exe.cmd failed (exit code $($b.ExitCode))." }
    }
    $mkShortcut = Join-Path $App 'make-shortcut.ps1'
    if (Test-Path $mkShortcut) {
        & $mkShortcut
    }

    # ---- 5. cleanup old node_modules (only now) ---------------------------
    Say '[5/5] Removing the old D:\DSH\node_modules ...'
    $oldNm = Join-Path $Root 'node_modules'
    if (Test-Path $oldNm) {
        $empty = Join-Path $env:TEMP ('dsh-empty-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $empty | Out-Null
        cmd.exe /c robocopy "$empty" "$oldNm" /MIR /NFL /NDL /NJH /NJS /NC | Out-Null
        Remove-Item -LiteralPath $oldNm -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $empty -Recurse -Force -ErrorAction SilentlyContinue
    }

    Say ''
    Say '============================================================'
    Say '  Migration done.'
    Say '    Workspace : D:\DSH           (your work files)'
    Say ('    Software  : ' + $App + '   (portable)')
    Say '  Double-click the desktop "DSH" shortcut to start.'
    Say '============================================================'

    # tidy up: move this script and the cmd wrapper into the app folder
    foreach ($f in $SelfNames) {
        $src = Join-Path $Root $f
        if (Test-Path $src) {
            try { Move-Item -LiteralPath $src -Destination $App -ErrorAction Stop } catch { }
        }
    }
}
catch {
    Write-Host ''
    Write-Host ('[ERROR] ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Nothing was deleted from D:\DSH\node_modules. If files were moved into DSH-App and you want to'
    Write-Host 'restore, move the contents of DSH-App back to D:\DSH and rerun install-dsh.cmd if needed.'
    exit 1
}
