Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceFile = Join-Path $root "resources\dashboard\favicon.ico"
$outDir = Join-Path $root "resources"
$appIconFile = Join-Path $outDir "app.ico"
$trayIconFile = Join-Path $outDir "tray.ico"

if (-not (Test-Path $sourceFile)) {
    throw "zashboard favicon not found: $sourceFile"
}

function Get-IcoImages {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + ($i * 16)
        $width = if ($bytes[$entry] -eq 0) { 256 } else { [int]$bytes[$entry] }
        $height = if ($bytes[$entry + 1] -eq 0) { 256 } else { [int]$bytes[$entry + 1] }
        $length = [BitConverter]::ToInt32($bytes, $entry + 8)
        $offset = [BitConverter]::ToInt32($bytes, $entry + 12)
        $imageBytes = New-Object byte[] $length
        [Array]::Copy($bytes, $offset, $imageBytes, 0, $length)

        [PSCustomObject]@{
            Width = $width
            Height = $height
            Bytes = $imageBytes
        }
    }
}

function Resize-PngBytes {
    param(
        [byte[]]$SourceBytes,
        [int]$Size
    )

    $sourceStream = [System.IO.MemoryStream]::new($SourceBytes)
    $sourceBitmap = [System.Drawing.Bitmap]::new($sourceStream)
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($sourceBitmap, 0, 0, $Size, $Size)

    $outStream = [System.IO.MemoryStream]::new()
    $bitmap.Save($outStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $result = $outStream.ToArray()

    $outStream.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    $sourceBitmap.Dispose()
    $sourceStream.Dispose()

    return ,$result
}

function ConvertTo-SolidColorPngBytes {
    param(
        [byte[]]$SourceBytes,
        [System.Drawing.Color]$Color
    )

    $sourceStream = [System.IO.MemoryStream]::new($SourceBytes)
    $sourceBitmap = [System.Drawing.Bitmap]::new($sourceStream)
    $bitmap = [System.Drawing.Bitmap]::new($sourceBitmap.Width, $sourceBitmap.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    for ($y = 0; $y -lt $sourceBitmap.Height; $y++) {
        for ($x = 0; $x -lt $sourceBitmap.Width; $x++) {
            $pixel = $sourceBitmap.GetPixel($x, $y)
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($pixel.A, $Color.R, $Color.G, $Color.B))
        }
    }

    $outStream = [System.IO.MemoryStream]::new()
    $bitmap.Save($outStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $result = $outStream.ToArray()

    $outStream.Dispose()
    $bitmap.Dispose()
    $sourceBitmap.Dispose()
    $sourceStream.Dispose()

    return ,$result
}

function New-AppIconPngBytes {
    param(
        [byte[]]$SourceBytes,
        [int]$Size
    )

    $sourceStream = [System.IO.MemoryStream]::new($SourceBytes)
    $sourceBitmap = [System.Drawing.Bitmap]::new($sourceStream)
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $backgroundInset = [Math]::Max(0, [int][Math]::Round($Size * 0.02))
    $backgroundSize = $Size - ($backgroundInset * 2)
    $radius = [Math]::Max(3, [int][Math]::Round($Size * 0.22))
    $backgroundRect = [System.Drawing.RectangleF]::new($backgroundInset, $backgroundInset, $backgroundSize, $backgroundSize)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($backgroundRect.Left, $backgroundRect.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($backgroundRect.Right - $diameter, $backgroundRect.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($backgroundRect.Right - $diameter, $backgroundRect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($backgroundRect.Left, $backgroundRect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    $backgroundBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 29, 29, 31))
    $graphics.FillPath($backgroundBrush, $path)

    $iconSize = [Math]::Max(1, [int][Math]::Round($Size * 0.62))
    $iconOffset = [Math]::Round(($Size - $iconSize) / 2)
    $graphics.DrawImage($sourceBitmap, $iconOffset, $iconOffset, $iconSize, $iconSize)

    $outStream = [System.IO.MemoryStream]::new()
    $bitmap.Save($outStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $result = $outStream.ToArray()

    $outStream.Dispose()
    $backgroundBrush.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    $sourceBitmap.Dispose()
    $sourceStream.Dispose()

    return ,$result
}

function Write-IcoFile {
    param(
        [string]$Path,
        [object[]]$Images
    )

    $writerStream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($writerStream)
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$Images.Count)

    $offset = 6 + (16 * $Images.Count)
    foreach ($image in $Images) {
        $writer.Write([byte]($(if ($image.Size -eq 256) { 0 } else { $image.Size })))
        $writer.Write([byte]($(if ($image.Size -eq 256) { 0 } else { $image.Size })))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$image.Bytes.Length)
        $writer.Write([UInt32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $Images) {
        $writer.Write($image.Bytes)
    }

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($Path, $writerStream.ToArray())
    $writer.Dispose()
    $writerStream.Dispose()
}

$sourceImages = @(Get-IcoImages -Path $sourceFile)
$largest = $sourceImages | Sort-Object Width -Descending | Select-Object -First 1
if ($null -eq $largest) {
    throw "No images found in zashboard favicon: $sourceFile"
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    [PSCustomObject]@{
        Size = $size
        Bytes = Resize-PngBytes -SourceBytes $largest.Bytes -Size $size
    }
}

$whiteImages = foreach ($image in $images) {
    [PSCustomObject]@{
        Size = $image.Size
        Bytes = ConvertTo-SolidColorPngBytes -SourceBytes $image.Bytes -Color ([System.Drawing.Color]::White)
    }
}

$appImages = foreach ($image in $whiteImages) {
    [PSCustomObject]@{
        Size = $image.Size
        Bytes = New-AppIconPngBytes -SourceBytes $image.Bytes -Size $image.Size
    }
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Write-IcoFile -Path $appIconFile -Images $appImages
Write-IcoFile -Path $trayIconFile -Images $whiteImages

Write-Host "Synced app icon to $appIconFile"
Write-Host "Synced tray icon to $trayIconFile"
