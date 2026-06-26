param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sourceRoot = Join-Path $repoRoot 'dashboard-src'
$dashboardDir = Join-Path $repoRoot 'resources\dashboard'

if (-not (Test-Path (Join-Path $sourceRoot 'package.json'))) {
    throw "dashboard-src is missing. Restore the zashboard source before building."
}

$requiredFiles = @(
    'src\hostBootstrap.ts',
    'src\views\CorePage.vue',
    'src\router\index.ts',
    'src\constant\index.ts'
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $sourceRoot $relativePath
    if (-not (Test-Path $path)) {
        throw "dashboard source check failed: missing $relativePath"
    }
}

$mainTsPath = Join-Path $sourceRoot 'src\main.ts'
$mainTs = Get-Content -LiteralPath $mainTsPath -Raw
if ($mainTs -notmatch "import\s+['""]\./hostBootstrap['""]") {
    throw "dashboard source check failed: src\main.ts must import ./hostBootstrap for desktop window drag/resize and host state"
}

& (Join-Path $repoRoot 'tools\check-dashboard-css-contract.ps1')

if ($SkipBuild) {
    Write-Host 'dashboard source check completed.'
    return
}

$pnpmCommand = Get-Command pnpm -ErrorAction SilentlyContinue
$pnpmPath = if ($pnpmCommand) { $pnpmCommand.Source } else { Join-Path $env:APPDATA 'npm\pnpm.cmd' }
if (-not (Test-Path $pnpmPath)) {
    throw "pnpm is required for local dashboard builds. Install pnpm 10.15.0, for example: npm install -g pnpm@10.15.0"
}

Push-Location $sourceRoot
try {
    $pnpmStoreDir = Join-Path $repoRoot '.tmp\pnpm-store'
    New-Item -ItemType Directory -Force -Path $pnpmStoreDir | Out-Null
    $env:PNPM_HOME = if ($env:PNPM_HOME) { $env:PNPM_HOME } else { Join-Path $env:APPDATA 'pnpm' }
    $env:PNPM_STORE_DIR = $pnpmStoreDir
    $env:npm_config_store_dir = $pnpmStoreDir

    & $pnpmPath install --frozen-lockfile --store-dir $pnpmStoreDir
    if ($LASTEXITCODE -ne 0) {
        throw "pnpm install failed with exit code $LASTEXITCODE"
    }

    $vitePath = Join-Path $sourceRoot 'node_modules\.bin\vite.cmd'
    if (-not (Test-Path $vitePath)) {
        throw "vite is missing. Run pnpm install in dashboard-src."
    }

    & $vitePath build
    if ($LASTEXITCODE -ne 0) {
        throw "dashboard build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

if (Test-Path $dashboardDir) {
    Remove-Item -LiteralPath $dashboardDir -Recurse -Force
}

New-Item -ItemType Directory -Path $dashboardDir | Out-Null
Copy-Item -Path (Join-Path $sourceRoot 'dist\*') -Destination $dashboardDir -Recurse -Force

& (Join-Path $repoRoot 'tools\create-app-icon.ps1')
