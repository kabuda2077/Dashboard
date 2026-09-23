param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$NuGetSource = 'https://api.nuget.org/v3/index.json',
    [switch]$SkipDashboardBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location $repoRoot

Write-Host "==> Building Dashboard" -ForegroundColor Cyan
Write-Host "    Configuration: $Configuration" -ForegroundColor Gray
Write-Host "    Runtime: $Runtime" -ForegroundColor Gray
Write-Host ""

if (-not $SkipDashboardBuild) {
    Write-Host "==> Step 1/3: Building dashboard UI" -ForegroundColor Cyan
    $dashboardBuildArgs = @('-ExecutionPolicy', 'Bypass', '-File', '.\tools\build-zashboard.ps1')
    powershell @dashboardBuildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dashboard UI build failed with exit code $LASTEXITCODE"
    }
    Write-Host ""
}
else {
    Write-Host "==> Step 1/3: Skipping dashboard UI build" -ForegroundColor Yellow
    Write-Host ""
}

& (Join-Path $repoRoot 'tools\assert-dashboard-assets.ps1') -Path (Join-Path $repoRoot 'resources\dashboard')

$publishDir = Join-Path $repoRoot "artifacts\publish\Dashboard-$Configuration-$Runtime"
if (Test-Path $publishDir) {
    Write-Host "==> Step 2/3: Cleaning publish directory" -ForegroundColor Cyan
    Remove-Item -LiteralPath $publishDir -Recurse -Force
    Write-Host ""
}

Write-Host "==> Step 3/3: Publishing .NET app" -ForegroundColor Cyan
dotnet restore -s $NuGetSource --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}
dotnet publish -c $Configuration -r $Runtime --no-restore --nologo --verbosity quiet `
    -o $publishDir `
    --self-contained false `
    /p:DebugType=None `
    /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'Dashboard.exe') -PathType Leaf)) {
    throw "Published Dashboard.exe is missing: $publishDir"
}
& (Join-Path $repoRoot 'tools\assert-dashboard-assets.ps1') -Path (Join-Path $publishDir 'resources\dashboard')

$runtimeDir = Join-Path $publishDir 'runtimes'
$rootWebViewLoader = Join-Path $publishDir 'WebView2Loader.dll'
if ((Test-Path $runtimeDir) -and (Test-Path $rootWebViewLoader)) {
    Remove-Item -LiteralPath $runtimeDir -Recurse -Force
}

Write-Host ""
Write-Host "Publish completed: $publishDir" -ForegroundColor Green
