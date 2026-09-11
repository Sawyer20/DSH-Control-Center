# ============================================================================
#  DSH one-click setup (no administrator rights required)
#
#  1. Ensure Node.js >= 20:
#       - reuse  .tools\node\node.exe        if present and usable
#       - reuse  node.exe from PATH           if present and >= 20
#       - else   download a portable Node.js LTS (zip) into .tools\node
#  2. Ensure pnpm >= 10:
#       - install a LOCAL copy under .tools\pnpm-home via npm (no -g, no admin)
#  3. Run  pnpm install  (reads .npmrc -> npmmirror, pnpm-workspace.yaml)
#  4. Create the "DSH" desktop shortcut
#
#  Compatible with Windows PowerShell 5.1 and PowerShell 7.
# ============================================================================
[CmdletBinding()]
param(
    [switch]$SkipInstall   # only ensure the toolchain, skip "pnpm install"
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$Root     = $PSScriptRoot
$Tools    = Join-Path $Root '.tools'
$NodeDir  = Join-Path $Tools 'node'
$PnpmHome = Join-Path $Tools 'pnpm-home'
$NodeExe  = Join-Path $NodeDir 'node.exe'
$NpmCli   = $null   # resolved below, after we know which node.exe is used
$PnpmCjs  = Join-Path $PnpmHome 'node_modules\pnpm\bin\pnpm.cjs'

$MirrorIndex    = 'https://registry.npmmirror.com/-/binary/node/index.json'
$MirrorDist     = 'https://registry.npmmirror.com/-/binary/node'
$OfficialIndex  = 'https://nodejs.org/dist/index.json'
$OfficialDist   = 'https://nodejs.org/dist'
$PnpmRegistry   = 'https://registry.npmmirror.com/'
$PnpmFallback   = 'https://registry.npmjs.org/'

function Write-Step([string]$msg) { Write-Host ''; Write-Host "==> $msg" -ForegroundColor Cyan }

function Test-NodeOk([string]$exe) {
    if (-not $exe -or -not (Test-Path -LiteralPath $exe)) { return $false }
    try {
        $v = & $exe --version 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $v) { return $false }
        if ($v -notmatch '^v(\d+)\.') { return $false }
        return ([int]$Matches[1] -ge 20)
    } catch { return $false }
}

# --- offline Node from the shipped installer ---------------------------------
# pack-for-other-pc copies the workspace-root node-v*-x64.msi into redist\.
# `msiexec /a` is an ADMINISTRATIVE install: it only unpacks the files to a
# target folder and needs no administrator rights (verified with Node 24).
function Find-ShippedNodeMsi {
    foreach ($r in @($Root, (Join-Path $Root 'redist'))) {
        if (-not (Test-Path -LiteralPath $r)) { continue }
        $m = Get-ChildItem -LiteralPath $r -Filter 'node-v*-x64.msi' -File -ErrorAction SilentlyContinue |
             Select-Object -First 1
        if ($m) { return $m.FullName }
    }
    return $null
}

function Expand-ShippedNodeMsi([string]$Msi, [string]$Dest) {
    $tmp = Join-Path $Tools '_node_msi'
    try {
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $tmp | Out-Null
        $proc = Start-Process -FilePath 'msiexec.exe' `
            -ArgumentList @('/a', $Msi, '/qn', "TARGETDIR=$tmp") -Wait -PassThru -NoNewWindow
        if ($proc.ExitCode -ne 0) { return $false }
        $exe = Get-ChildItem -LiteralPath $tmp -Recurse -File -Filter 'node.exe' -ErrorAction SilentlyContinue |
               Select-Object -First 1
        if (-not $exe) { return $false }
        if (Test-Path -LiteralPath $Dest) { Remove-Item -LiteralPath $Dest -Recurse -Force }
        Move-Item -LiteralPath (Split-Path -Parent $exe.FullName) -Destination $Dest
        return $true
    } catch { return $false }
    finally { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}

function Find-LtsNodeZip {
    $sources = @(
        @{ index = $MirrorIndex; dist = $MirrorDist },
        @{ index = $OfficialIndex; dist = $OfficialDist }
    )
    foreach ($s in $sources) {
        try {
            Write-Host "    (version list: $($s.index))"
            $idx = Invoke-RestMethod -Uri $s.index -TimeoutSec 40
            $entry = $null
            foreach ($e in $idx) {
                if ($e.lts -and $e.version -match '^v(\d+)\.') {
                    if ([int]$Matches[1] -ge 20) { $entry = $e; break }
                }
            }
            if (-not $entry) { continue }
            $file = $entry.files | Where-Object { $_.url -like '*win-x64.zip' } | Select-Object -First 1
            if (-not $file) { continue }
            $name = [IO.Path]::GetFileName([string]$file.url)
            return @{ version = [string]$entry.version; name = $name; url = "$($s.dist)/$($entry.version)/$name" }
        } catch {
            Write-Host "    (cannot reach version list: $($_.Exception.Message))"
        }
    }
    return $null
}

# ---------------------------------------------------------------------------
# 1. Node.js
# ---------------------------------------------------------------------------
$node = $null
if (Test-NodeOk $NodeExe) {
    $node = $NodeExe
} else {
    $cmd = Get-Command node -ErrorAction SilentlyContinue
    if ($cmd -and (Test-NodeOk $cmd.Source)) { $node = $cmd.Source }
}

if (-not $node) {
    # Offline path first: unpack the shipped Node installer (redist\node-*.msi).
    $shipped = Find-ShippedNodeMsi
    if ($shipped) {
        Write-Step ('Step 1/4  Node.js missing - unpacking shipped installer ' + [IO.Path]::GetFileName($shipped))
        Write-Host '    portable unpack (msiexec /a): nothing is installed system-wide,'
        Write-Host '    no admin rights required. Do NOT use the installer option'
        Write-Host '    "Tools for Native Modules" - it downloads Python + Visual Studio'
        Write-Host '    Build Tools (several GB) and is useless here: every native module'
        Write-Host '    in this project ships as a prebuilt binary.'
        if (Expand-ShippedNodeMsi -Msi $shipped -Dest $NodeDir) {
            if (Test-NodeOk $NodeExe) { $node = $NodeExe }
            else { Write-Host '    unpacked Node failed to run, falling back to download' }
        } else {
            Write-Host '    unpacking failed, falling back to download'
        }
    }
}

if (-not $node) {
    Write-Step 'Step 1/4  Node.js is missing or older than v20.'
    Write-Step '         Downloading a portable Node.js LTS (zip, ~35 MB) into .tools\node ...'
    $info = Find-LtsNodeZip
    if (-not $info) { throw 'Could not fetch the Node.js version list (npmmirror + nodejs.org both failed). Check your network.' }

    New-Item -ItemType Directory -Force -Path $Tools | Out-Null
    $zip = Join-Path $Tools $info.name
    Write-Host "    url : $($info.url)"
    Write-Host '    downloading ...'
    $downloaded = $false
    try {
        Invoke-WebRequest -Uri $info.url -OutFile $zip -UseBasicParsing -TimeoutSec 600
        $downloaded = $true
    } catch {
        Write-Host "    download failed: $($_.Exception.Message)"
    }
    if (-not $downloaded) { throw "Node.js download failed: $($info.url)" }

    $tmp = Join-Path $Tools '_node_extract'
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $tmp -Force
    $inner = Get-ChildItem -LiteralPath $tmp -Directory | Select-Object -First 1
    if (-not $inner) { throw 'Unexpected layout inside the Node.js archive.' }
    if (Test-Path -LiteralPath $NodeDir) { Remove-Item -LiteralPath $NodeDir -Recurse -Force }
    Move-Item -LiteralPath $inner.FullName -Destination $NodeDir
    Remove-Item -LiteralPath $tmp -Recurse -Force
    Remove-Item -LiteralPath $zip -Force

    $node = $NodeExe
    if (-not (Test-NodeOk $node)) { throw 'The downloaded Node.js cannot run. Aborting.' }
}
$nodeVersion = & $node --version
Write-Step "Step 1/4  Node.js ready: $node  ($nodeVersion)"

# npm CLI always lives next to the node.exe that is actually used
$nodeHome = Split-Path -Parent $node
$NpmCli   = Join-Path $nodeHome 'node_modules\npm\bin\npm-cli.js'
if (-not (Test-Path -LiteralPath $NpmCli)) { throw "npm was not found next to Node.js: $NpmCli" }

# ---------------------------------------------------------------------------
# 2. pnpm (local copy under .tools, never global -> no admin needed)
# ---------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $PnpmCjs)) {
    Write-Step 'Step 2/4  Installing a local pnpm under .tools\pnpm-home ...'
    New-Item -ItemType Directory -Force -Path $PnpmHome | Out-Null
    & $node $NpmCli install --prefix $PnpmHome --no-audit --no-fund --registry $PnpmRegistry pnpm@11
    if ($LASTEXITCODE -ne 0) {
        Write-Host '    first registry failed, retrying with the official npm registry ...'
        & $node $NpmCli install --prefix $PnpmHome --no-audit --no-fund --registry $PnpmFallback pnpm@11
    }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $PnpmCjs)) {
        throw 'Failed to install pnpm. Check your network and retry.'
    }
}
$pnpmVersion = & $node $PnpmCjs --version
Write-Step "Step 2/4  pnpm ready: $PnpmCjs  ($pnpmVersion)"

# ---------------------------------------------------------------------------
# 3. DSH dependencies
# ---------------------------------------------------------------------------
if ($SkipInstall) {
    Write-Step 'Step 3/4  Skipped (parameter -SkipInstall).'
} else {
    Write-Step 'Step 3/4  Installing DSH dependencies (pnpm install, mirror source) ...'
    Write-Host '    this downloads all packages; native modules (node-pty / koffi / sharp)'
    Write-Host '    come as prebuilt binaries - no C++ compiler or Python is needed.'
    Write-Host '    it can take several minutes on the first run.'
    Push-Location $Root
    try {
        & $node $PnpmCjs install --reporter append-only
        if ($LASTEXITCODE -ne 0) { throw "pnpm install failed with exit code $LASTEXITCODE." }
    } finally { Pop-Location }
}

# ---------------------------------------------------------------------------
# 4. Desktop shortcut
# ---------------------------------------------------------------------------
Write-Step 'Step 4/4  Creating the desktop shortcut ...'
& (Join-Path $Root 'make-shortcut.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Could not create the desktop shortcut.' }

# ---------------------------------------------------------------------------
# 5. Default workspace
#    The backend is started in a folder, and that folder is the workspace a
#    session gets when the web UI is not told otherwise (DSH itself asks you to
#    pick a workspace per session). Create a purpose-named EMPTY folder for it,
#    so a session never ends up inside the program's own directory.
# ---------------------------------------------------------------------------
$defaultWs = Join-Path $Root 'default-workspace'
if (-not (Test-Path -LiteralPath $defaultWs)) {
    New-Item -ItemType Directory -Force -Path $defaultWs | Out-Null
}
Write-Host "  Default workspace ready: $defaultWs"

Write-Host ''
Write-Host '============================================================'
Write-Host '  All done. Double-click the "DSH" shortcut on the desktop.'
Write-Host '============================================================'
Write-Host ''
