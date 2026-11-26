param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\LightningStart\Assets\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-RoundedPath {
    param([int]$x, [int]$y, [int]$w, [int]$h, [int]$r)

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = [Math]::Max(1, $r * 2)
    if ($d -gt $w) { $d = $w }
    if ($d -gt $h) { $d = $h }

    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

$images = [System.Collections.Generic.List[System.Drawing.Bitmap]]::new()

foreach ($size in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $bgRadius = [Math]::Max(2, [int]($size * 0.24))
    $bgPath = New-RoundedPath -x 0 -y 0 -w $size -h $size -r $bgRadius
    $rect = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 29, 78, 216),
        [System.Drawing.Color]::FromArgb(255, 59, 130, 246),
        45.0)
    $graphics.FillPath($brush, $bgPath)
    $brush.Dispose()
    $bgPath.Dispose()

    # White lightning bolt: quick launch mark.
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $bolt = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $boltPoints = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(0.57 * $size, 0.08 * $size),
        [System.Drawing.PointF]::new(0.28 * $size, 0.55 * $size),
        [System.Drawing.PointF]::new(0.46 * $size, 0.55 * $size),
        [System.Drawing.PointF]::new(0.38 * $size, 0.92 * $size),
        [System.Drawing.PointF]::new(0.72 * $size, 0.42 * $size),
        [System.Drawing.PointF]::new(0.52 * $size, 0.42 * $size)
    )
    $bolt.AddPolygon($boltPoints)
    $graphics.FillPath($white, $bolt)

    $bolt.Dispose()
    $white.Dispose()
    $graphics.Dispose()
    $images.Add($bmp)
}

$entries = [System.Collections.Generic.List[byte[]]]::new()
$blobs = [System.Collections.Generic.List[byte[]]]::new()

foreach ($bmp in $images) {
    $w = $bmp.Width
    $h = $bmp.Height
    $rect = [System.Drawing.Rectangle]::new(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $raw = [byte[]]::new($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    $bmp.UnlockBits($data)

    $pixels = [byte[]]::new($w * $h * 4)
    for ($y = 0; $y -lt $h; $y++) {
        $srcOffset = ($h - 1 - $y) * $stride
        [Array]::Copy($raw, $srcOffset, $pixels, $y * $w * 4, $w * 4)
    }

    $maskStride = [int]([Math]::Ceiling($w / 32.0) * 4)
    $mask = [byte[]]::new($maskStride * $h)

    $header = [byte[]]::new(40)
    [BitConverter]::GetBytes([int]40).CopyTo($header, 0)
    [BitConverter]::GetBytes([int]$w).CopyTo($header, 4)
    [BitConverter]::GetBytes([int]($h * 2)).CopyTo($header, 8)
    [BitConverter]::GetBytes([int16]1).CopyTo($header, 12)
    [BitConverter]::GetBytes([int16]32).CopyTo($header, 14)
    [BitConverter]::GetBytes([int]0).CopyTo($header, 16)
    [BitConverter]::GetBytes([int]($pixels.Length + $mask.Length)).CopyTo($header, 20)

    $blob = [byte[]]::new(40 + $pixels.Length + $mask.Length)
    [Array]::Copy($header, 0, $blob, 0, 40)
    [Array]::Copy($pixels, 0, $blob, 40, $pixels.Length)
    [Array]::Copy($mask, 0, $blob, 40 + $pixels.Length, $mask.Length)

    $entry = [byte[]]::new(16)
    $entry[0] = if ($w -ge 256) { [byte]0 } else { [byte]$w }
    $entry[1] = if ($h -ge 256) { [byte]0 } else { [byte]$h }
    $entry[2] = [byte]0
    $entry[3] = [byte]0
    [BitConverter]::GetBytes([int16]1).CopyTo($entry, 4)
    [BitConverter]::GetBytes([int16]32).CopyTo($entry, 6)
    [BitConverter]::GetBytes([int]$blob.Length).CopyTo($entry, 8)

    $entries.Add($entry)
    $blobs.Add($blob)
}

$offset = 6 + 16 * $entries.Count
for ($i = 0; $i -lt $entries.Count; $i++) {
    [BitConverter]::GetBytes([int]$offset).CopyTo($entries[$i], 12)
    $offset += $blobs[$i].Length
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$stream = [System.IO.File]::Create($OutputPath)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([int16]0)
$writer.Write([int16]1)
$writer.Write([int16]$entries.Count)
foreach ($entry in $entries) { $writer.Write($entry) }
foreach ($blob in $blobs) { $writer.Write($blob) }
$writer.Flush()
$writer.Dispose()
$stream.Dispose()

$count = $images.Count
foreach ($bmp in $images) { $bmp.Dispose() }

$icon = [System.Drawing.Icon]::new($OutputPath)
Write-Host "Generated $OutputPath with $count sizes (first frame $($icon.Width)x$($icon.Height))."
$icon.Dispose()
