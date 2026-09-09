# ============================================================================
#  png-to-ico.ps1 - high-quality PNG/JPG -> multi-size .ico (32-bit RGBA)
#
#  Runs on PowerShell 7 (pwsh) and Windows PowerShell 5.1. System.Drawing is
#  available in both on Windows - verified on pwsh 7.6.6 (3 frames / 7 frames).
#
#  NOTE: `-File` cannot carry a comma array: `-Sizes 16,32,48` arrives as the
#  single string "16,32,48", which binds to the number 163248 and makes GDI+
#  throw "Parameter is not valid" (same in 5.1). Either omit -Sizes, or wrap:
#    pwsh -NoProfile -File png-to-ico.ps1 -Source in.png -Output out.ico
#    pwsh -NoProfile -Command "& '.\png-to-ico.ps1' -Source in.png -Output out.ico -Sizes 16,32,48"
#
#  Quality rules implemented:
#    * every target size is scaled INDEPENDENTLY from the original image
#      (no cascaded 1254->256->128->64->32->16 downscaling);
#    * canvas is cleared to transparent before drawing -> no black/white back-
#      ground, alpha is preserved (frames are 32-bit RGBA PNGs);
#    * HighQualityBicubic + HighQuality compositing.
# ============================================================================

param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [string]$Output = '',
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

$ErrorActionPreference = 'Stop'

if (-not $Output) {
    $outName = [System.IO.Path]::GetFileNameWithoutExtension($Source) + '.ico'
    $Output = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) $outName
}

try {
    Add-Type -AssemblyName System.Drawing
} catch {
    throw "System.Drawing is unavailable - usually a broken .NET install. Retry with pwsh 7 or Windows PowerShell 5.1: -NoProfile -File `"$($MyInvocation.MyCommand.Definition)`""
}

if (-not (Test-Path -LiteralPath $Source)) { throw "Source not found: $Source" }
$src = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source))

$sizes = @($Sizes | Sort-Object -Unique)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'

foreach ($s in $sizes) {
    # draw onto a square, fully transparent, 32-bit ARGB bitmap
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

$dir = Split-Path -Parent $Output
if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type: icon
$bw.Write([uint16]$frames.Count)     # image count

$offset = 6 + 16 * $frames.Count
for ($i = 0; $i -lt $frames.Count; $i++) {
    $s = $sizes[$i]
    $dim = $s
    if ($dim -eq 256) { $dim = 0 }   # 0 means 256
    $bw.Write([byte]$dim)            # width
    $bw.Write([byte]$dim)            # height
    $bw.Write([byte]0)               # colors
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # planes
    $bw.Write([uint16]32)            # bit count (32-bit RGBA)
    $bw.Write([uint32]$frames[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write($f) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Output, $out.ToArray())
$bw.Dispose(); $out.Dispose()

Write-Output ("OK: " + $Output + "  sizes=[" + ($sizes -join ',') + "]  (PNG frames, 32-bit RGBA)")
