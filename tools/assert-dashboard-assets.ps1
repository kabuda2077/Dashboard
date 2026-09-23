param(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($Path)
$indexPath = Join-Path $root 'index.html'
if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Dashboard UI is missing: $indexPath. Build the frontend before building or publishing .NET."
}

# Validate the HTML entry and its local scripts, styles, icons and preloads.
# This is not a recursive JS/CSS dependency parser or a freshness guarantee.
$html = [System.IO.File]::ReadAllText($indexPath)
if ([string]::IsNullOrWhiteSpace($html)) {
    throw "Dashboard UI entry is empty: $indexPath"
}
$references = [regex]::Matches($html, '(?i)\b(?:src|href)\s*=\s*["'']([^"'']+)["'']')
foreach ($match in $references) {
    $reference = $match.Groups[1].Value
    if ($reference -match '^(?:[a-z][a-z0-9+.-]*:|//|#)') { continue }
    $relative = [Uri]::UnescapeDataString(($reference -split '[?#]', 2)[0]).TrimStart('/')
    if ([string]::IsNullOrWhiteSpace($relative)) { continue }
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $root $relative))
    $prefix = $root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Dashboard UI reference escapes its resource directory: $reference"
    }
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Dashboard UI references a missing file: $reference"
    }
}
