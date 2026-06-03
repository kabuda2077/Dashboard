Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceFile = Join-Path $root "resources\dashboard\favicon.ico"
$outDir = Join-Path $root "resources"
$outFile = Join-Path $outDir "app.ico"

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

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$writerStream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($writerStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($image in $images) {
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

foreach ($image in $images) {
    $writer.Write($image.Bytes)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($outFile, $writerStream.ToArray())
$writer.Dispose()
$writerStream.Dispose()

Write-Host "Synced zashboard icon to $outFile"
