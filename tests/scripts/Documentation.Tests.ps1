#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
# Check current documentation links, not historical ledgers or implementation claims.
$required = @('AGENTS.md', 'docs/development.md', 'docs/upstream.md')
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
        throw "Missing project document: $relative"
    }
}
$documents = @('AGENTS.md', 'README.md', 'README.en.md') + @(Get-ChildItem (Join-Path $root 'docs') -Recurse -File -Filter *.md | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) })
foreach ($relative in $documents) {
    $path = Join-Path $root $relative
    $text = [IO.File]::ReadAllText($path)
    foreach ($match in [regex]::Matches($text, '\[[^\]]*\]\(([^\s)]+)\)')) {
        $link = $match.Groups[1].Value
        if ($link -match '^(https?://|mailto:)') { continue }
        $parts = $link -split '#', 2
        $target = [Uri]::UnescapeDataString($parts[0])
        $targetPath = if ($target) { Join-Path (Split-Path $path) $target } else { $path }
        if (-not (Test-Path -LiteralPath $targetPath)) {
            throw "Broken current documentation link: $relative -> $link"
        }
        if ($parts.Count -eq 2 -and $parts[1] -and [IO.Path]::GetExtension($targetPath) -eq '.md') {
            $fragment = [Uri]::UnescapeDataString($parts[1])
            $targetText = [IO.File]::ReadAllText($targetPath)
            $anchors = @([regex]::Matches($targetText, '<a\s+(?:id|name)=["'']([^"'']+)["'']') | ForEach-Object { $_.Groups[1].Value })
            $headingIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($heading in [regex]::Matches($targetText, '(?m)^#{1,6}\s+(.+?)\s*#*\s*$')) {
                $slug = ($heading.Groups[1].Value.ToLowerInvariant() -replace '[^\p{L}\p{N}\p{M}_ \-]', '') -replace ' ', '-'
                $id = $slug; $suffix = 0
                while (-not $headingIds.Add($id)) { $suffix++; $id = "$slug-$suffix" }
            }
            if ($fragment -cnotin $anchors -and -not $headingIds.Contains($fragment)) {
                throw "Broken documentation section: $relative -> $link"
            }
        }
    }
}
$project = [xml][IO.File]::ReadAllText((Join-Path $root 'Dashboard.csproj'))
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$informational = $project.SelectSingleNode('/Project/PropertyGroup/InformationalVersion').InnerText
if ($version -ne $informational) { throw 'Version and InformationalVersion disagree.' }
Write-Host 'Current documentation links and application versions are consistent.'
