param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$NuGetSource = 'https://api.nuget.org/v3/index.json',
    [switch]$SkipDashboardBuild = $false
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

Write-Host "==> Building mihomo-dashboard..." -ForegroundColor Cyan
Write-Host "    Configuration: $Configuration" -ForegroundColor Gray
Write-Host "    Runtime: $Runtime" -ForegroundColor Gray
Write-Host ""

# 构建 zashboard 前端
if (-not $SkipDashboardBuild) {
    Write-Host "==> Step 1/3: Building zashboard frontend..." -ForegroundColor Cyan
    $dashboardBuildStart = Get-Date
    powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1
    $dashboardBuildTime = (Get-Date) - $dashboardBuildStart
    Write-Host "    Frontend build completed in $($dashboardBuildTime.TotalSeconds.ToString('F2'))s" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "==> Skipping frontend build (--SkipDashboardBuild)" -ForegroundColor Yellow
    Write-Host ""
}

# 清理发布目录
$publishDir = Join-Path $repoRoot "bin\$Configuration\net9.0-windows\$Runtime\publish"
if (Test-Path $publishDir) {
    Write-Host "==> Step 2/3: Cleaning publish directory..." -ForegroundColor Cyan
    Remove-Item -LiteralPath $publishDir -Recurse -Force
    Write-Host "    Cleaned: $publishDir" -ForegroundColor Green
    Write-Host ""
}

# 还原依赖并发布
Write-Host "==> Step 3/3: Publishing .NET application..." -ForegroundColor Cyan
$publishStart = Get-Date

Write-Host "    Restoring NuGet packages..." -ForegroundColor Gray
dotnet restore -s $NuGetSource --nologo --verbosity quiet

Write-Host "    Publishing optimized build..." -ForegroundColor Gray
dotnet publish -c $Configuration -r $Runtime --no-restore --nologo --verbosity quiet `
    --self-contained false `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    /p:PublishReadyToRun=true `
    /p:TieredCompilation=true

$publishTime = (Get-Date) - $publishStart
Write-Host "    .NET build completed in $($publishTime.TotalSeconds.ToString('F2'))s" -ForegroundColor Green
Write-Host ""

# 输出统计信息
Write-Host "==> Build Summary" -ForegroundColor Cyan
Write-Host "    Output directory: $publishDir" -ForegroundColor Gray

if (Test-Path $publishDir) {
    $files = Get-ChildItem -Path $publishDir -Recurse -File
    $totalSize = ($files | Measure-Object -Property Length -Sum).Sum
    $totalSizeMB = [math]::Round($totalSize / 1MB, 2)
    $fileCount = $files.Count

    Write-Host "    Total files: $fileCount" -ForegroundColor Gray
    Write-Host "    Total size: $totalSizeMB MB" -ForegroundColor Gray

    # 显示主要文件大小
    $mainExe = Join-Path $publishDir "Dashboard.exe"
    if (Test-Path $mainExe) {
        $exeSize = [math]::Round((Get-Item $mainExe).Length / 1KB, 2)
        Write-Host "    Dashboard.exe: $exeSize KB" -ForegroundColor Gray
    }

    $mainDll = Join-Path $publishDir "Dashboard.dll"
    if (Test-Path $mainDll) {
        $dllSize = [math]::Round((Get-Item $mainDll).Length / 1KB, 2)
        Write-Host "    Dashboard.dll: $dllSize KB" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "==> Build completed successfully! " -ForegroundColor Green -NoNewline
Write-Host "v" -ForegroundColor Green
Write-Host ""
Write-Host "To run the application:" -ForegroundColor Yellow
Write-Host "    cd $publishDir" -ForegroundColor Gray
Write-Host "    .\Dashboard.exe" -ForegroundColor Gray
Write-Host ""
