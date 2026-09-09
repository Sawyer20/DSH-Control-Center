# ============================================================================
#  gen-icons.ps1 - regenerate the DSH icon set.
#
#  SOURCE PNGs are read from .\icon-src\ (this folder).
#
#  Run (PowerShell 7 preferred; Windows PowerShell 5.1 also works):
#    pwsh -NoProfile -File D:\DSH\DSH-App\gen-icons.ps1
#
#  NOTE: kept deliberately pure ASCII so it parses identically under pwsh 7
#  (UTF-8 default) and Windows PowerShell 5.1 (ANSI when the file has no BOM).
#
#  Outputs (next to this script):
#    app.ico            sizes 16,20,24,32,40,48,64,128,256  (EXE / Form / shortcut)
#    tray_running.ico   sizes 16,20,24,32,48,64   (whale + green dot)
#    tray_starting.ico  sizes 16,20,24,32,48,64   (whale + amber dot)
#    tray_error.ico     sizes 16,20,24,32,48,64   (whale + red X)
#    tray_offline.ico   sizes 16,20,24,32,48,64   (grey whale)
#
#  Quality rules: every target size is scaled INDEPENDENTLY from the original
#  PNG (no cascaded downscaling); canvas is transparent; frames are 32-bit
#  RGBA PNGs (no black/white background).
# ============================================================================

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$dst = Split-Path -Parent $MyInvocation.MyCommand.Definition
$pngDir = Join-Path $dst 'icon-src'

function New-Ico([string]$Source, [string]$Output, [int[]]$Sizes) {
    $src = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source))
    $sizes = @($Sizes | Sort-Object -Unique)
    $frames = New-Object 'System.Collections.Generic.List[byte[]]'

    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($src, 0, 0, $s, $s)
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($ms.ToArray())
        $g.Dispose(); $bmp.Dispose(); $ms.Dispose()
    }
    $src.Dispose()

    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($out)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $dim = $sizes[$i]
        if ($dim -eq 256) { $dim = 0 }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$frames[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($f in $frames) { $bw.Write($f) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($Output, $out.ToArray())
    $bw.Dispose(); $out.Dispose()
    Write-Output ('OK  ' + [System.IO.Path]::GetFileName($Output) + '  sizes=[' + ($sizes -join ',') + ']')
}

$appSizes  = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$traySizes = @(16, 20, 24, 32, 48, 64)

New-Ico -Source (Join-Path $pngDir 'app.png')          -Output (Join-Path $dst 'app.ico')          -Sizes $appSizes
New-Ico -Source (Join-Path $pngDir 'tray_running.png') -Output (Join-Path $dst 'tray_running.ico') -Sizes $traySizes
New-Ico -Source (Join-Path $pngDir 'tray_starting.png') -Output (Join-Path $dst 'tray_starting.ico') -Sizes $traySizes
New-Ico -Source (Join-Path $pngDir 'tray_error.png')   -Output (Join-Path $dst 'tray_error.ico')   -Sizes $traySizes
New-Ico -Source (Join-Path $pngDir 'tray_offline.png') -Output (Join-Path $dst 'tray_offline.ico') -Sizes $traySizes

# whale_base.png stays a PNG (header logo asset); keep a copy next to the app
$wp = Join-Path $dst 'whale_base.png'
Copy-Item (Join-Path $pngDir 'whale_base.png') $wp -Force
Write-Output ('whale_base.png copied: ' + (Test-Path $wp))
