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

$publishDir = Join-Path $repoRoot "artifacts\publish\Dashboard-$Configuration-$Runtime"
if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

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
2. Extract the archive.
3. Copy all files to the existing Dashboard folder.
4. Run Dashboard.exe.
"@ | Out-File -FilePath $releaseNotesPath -Encoding UTF8

Write-Host "Release package created: $zipPath" -ForegroundColor Green
Write-Host "Release notes: $releaseNotesPath" -ForegroundColor Green
