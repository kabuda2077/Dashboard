#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
# Check current documentation links, not historical ledgers or implementation claims.
$required = @('docs/maintenance.md', 'docs/upstream-merge.md', 'docs/style.md')
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relative) -PathType Leaf)) {
        throw "Missing maintenance document: $relative"
    }
}
$documents = @('README.md', 'README.en.md') + @(Get-ChildItem (Join-Path $root 'docs') -Recurse -File -Filter *.md | ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName) })
foreach ($relative in $documents) {
    $path = Join-Path $root $relative
    $text = [IO.File]::ReadAllText($path)
    foreach ($match in [regex]::Matches($text, '\[[^\]]*\]\(([^\s)]+)\)')) {
        $link = $match.Groups[1].Value
        if ($link -match '^(https?://|mailto:|#)') { continue }
        $target = [Uri]::UnescapeDataString(($link -split '#', 2)[0])
        if ($target -and -not (Test-Path -LiteralPath (Join-Path (Split-Path $path) $target))) {
            throw "Broken current documentation link: $relative -> $link"
        }
    }
}
$project = [xml][IO.File]::ReadAllText((Join-Path $root 'Dashboard.csproj'))
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$informational = $project.SelectSingleNode('/Project/PropertyGroup/InformationalVersion').InnerText
if ($version -ne $informational) { throw 'Version and InformationalVersion disagree.' }
Write-Host 'Current documentation links and application versions are consistent.'
