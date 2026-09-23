param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputZip = "Dashboard-Release-$(Get-Date -Format 'yyyyMMdd-HHmmss').zip",
    [switch]$SkipDashboardBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$buildArgs = @(
    '-ExecutionPolicy', 'Bypass',
    '-File', '.\build.ps1',
    '-Configuration', $Configuration,
    '-Runtime', $Runtime
)

if ($SkipDashboardBuild) {
    $buildArgs += '-SkipDashboardBuild'
}

powershell @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Dashboard build failed with exit code $LASTEXITCODE; release packaging cancelled."
}

$publishDir = Join-Path $repoRoot "artifacts\publish\Dashboard-$Configuration-$Runtime"
if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'Dashboard.exe') -PathType Leaf)) {
    throw "Published Dashboard.exe is missing: $publishDir"
}
& (Join-Path $repoRoot 'tools\assert-dashboard-assets.ps1') -Path (Join-Path $publishDir 'resources\dashboard')

$releaseDir = Join-Path $repoRoot 'artifacts\releases'
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$zipPath = Join-Path $releaseDir $OutputZip
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
$releaseNotesPath = Join-Path $releaseDir "RELEASE_NOTES_$(Get-Date -Format 'yyyyMMdd').txt"

@"
Dashboard Release Package

Build Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Configuration: $Configuration
Runtime: $Runtime
Archive: $OutputZip
Size: $zipSize MB

Install:
1. Close Dashboard.
2. Extract the archive and copy all files to the existing Dashboard folder.
3. Confirm replacement. Do not delete the existing folder first.
4. Run Dashboard.exe.

The package does not contain settings.json, mihomo, sing-box, or runtime logs.
Packaged content updates invalidate HTTP/cache storage and service workers while preserving WebView settings, labels, and connection history. The local origin remains http://127.0.0.1:33291/; a port conflict is reported instead of changing origins.
"@ | Out-File -FilePath $releaseNotesPath -Encoding UTF8

Write-Host "Release package created: $zipPath" -ForegroundColor Green
Write-Host "Release notes: $releaseNotesPath" -ForegroundColor Green
