# Generates assets\MK.ico (PNG entries 16, 20, 24, 32, 40, 48, 64, 256) and assets\MK.png (256):
# a rounded dark keycap with an accent-blue play triangle.
# Run once (or when the artwork changes); the build and the installer use the committed files. Needs only System.Drawing.
param([string]$AssetsFolder = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $AssetsFolder -Force | Out-Null

function New-RoundedRect([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($R * 2)
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc([single]($X + $W - $d), $Y, $d, $d, 270, 90)
    $path.AddArc([single]($X + $W - $d), [single]($Y + $H - $d), $d, $d, 0, 90)
    $path.AddArc($X, [single]($Y + $H - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Draws one icon at its own pixel size, so small sizes get a bigger triangle and no fine detail.
function New-Artwork([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $Size / 256.0
    $small = $Size -le 32

    # Keycap body: dark gradient, almost edge to edge.
    $inset = [single]([Math]::Max(0.5, 6 * $s))
    $bodySize = [single]($Size - 2 * $inset)
    $body = New-RoundedRect $inset $inset $bodySize $bodySize ([single]($bodySize * 0.22))
    $rect = New-Object System.Drawing.RectangleF 0, 0, $Size, $Size
    $bodyBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 54, 58, 68)), ([System.Drawing.Color]::FromArgb(255, 24, 26, 32)), 90.0
    $g.FillPath($bodyBrush, $body)

    # Keycap top face: a slightly lighter inset panel (skipped at tiny sizes where it would only blur).
    if (-not $small) {
        $faceInset = [single](30 * $s)
        $faceSize = [single]($Size - 2 * $faceInset)
        $faceTop = [single](24 * $s)
        $face = New-RoundedRect $faceInset $faceTop $faceSize ([single]($faceSize - 14 * $s)) ([single]($faceSize * 0.17))
        $faceBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 70, 75, 88)), ([System.Drawing.Color]::FromArgb(255, 40, 43, 52)), 90.0
        $g.FillPath($faceBrush, $face)
        $faceBrush.Dispose(); $face.Dispose()
    }

    # Play triangle in accent blue, optically centred (a triangle's centroid sits left of its bounding box centre).
    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 76, 168, 255))
    $half = [single]($(if ($small) { 0.27 } else { 0.22 }) * $Size)
    $cx = [single](0.5 * $Size + 0.03 * $Size)
    $cy = [single]$(if ($small) { 0.5 * $Size } else { 0.47 * $Size })
    $points = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([single]($cx - $half * 0.9), [single]($cy - $half))),
        (New-Object System.Drawing.PointF ([single]($cx + $half * 1.1), $cy)),
        (New-Object System.Drawing.PointF ([single]($cx - $half * 0.9), [single]($cy + $half)))
    )
    $g.FillPolygon($accent, $points)
    $accent.Dispose(); $bodyBrush.Dispose(); $body.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = foreach ($size in $sizes) { $b = New-Artwork $size; $ms = New-Object IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $AssetsFolder 'MK.ico'), $out.ToArray())
[IO.File]::WriteAllBytes((Join-Path $AssetsFolder 'MK.png'), $pngs[-1])
Write-Output "Wrote $(Join-Path $AssetsFolder 'MK.ico') and MK.png"
