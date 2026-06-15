param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$NuGetSource = 'https://api.nuget.org/v3/index.json',
    [switch]$SkipDashboardBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

Write-Host "==> Building Dashboard" -ForegroundColor Cyan
Write-Host "    Configuration: $Configuration" -ForegroundColor Gray
Write-Host "    Runtime: $Runtime" -ForegroundColor Gray
Write-Host ""

if (-not $SkipDashboardBuild) {
    Write-Host "==> Step 1/3: Building dashboard UI" -ForegroundColor Cyan
    powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1
    Write-Host ""
}
else {
    Write-Host "==> Step 1/3: Skipping dashboard UI build" -ForegroundColor Yellow
    Write-Host ""
}

$publishDir = Join-Path $repoRoot "artifacts\publish\Dashboard-$Configuration-$Runtime"
if (Test-Path $publishDir) {
    Write-Host "==> Step 2/3: Cleaning publish directory" -ForegroundColor Cyan
    Remove-Item -LiteralPath $publishDir -Recurse -Force
    Write-Host ""
}

Write-Host "==> Step 3/3: Publishing .NET app" -ForegroundColor Cyan
dotnet restore -s $NuGetSource --nologo --verbosity quiet
dotnet publish -c $Configuration -r $Runtime --no-restore --nologo --verbosity quiet `
    -o $publishDir `
    --self-contained false `
    /p:DebugType=None `
    /p:DebugSymbols=false

Write-Host ""
Write-Host "Publish completed: $publishDir" -ForegroundColor Green
