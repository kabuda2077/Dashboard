param(
    [string]$Configuration = 'Debug',
    [switch]$SkipDotnetBuild,
    [switch]$SkipDotnetTests,
    [switch]$SkipFrontendTypeCheck,
    [switch]$SkipFrontendTests,
    [switch]$SkipFrontendBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dashboardRoot = Join-Path $repoRoot 'dashboard-src'
$pnpmStoreDir = Join-Path $repoRoot '.tmp\pnpm-store'

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Script
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Script
    Write-Host "    OK" -ForegroundColor Green
    Write-Host ""
}

function Get-PnpmPath {
    $pnpmCommand = Get-Command pnpm -ErrorAction SilentlyContinue
    if ($pnpmCommand) {
        return $pnpmCommand.Source
    }

    $fallback = Join-Path $env:APPDATA 'npm\pnpm.cmd'
    if (Test-Path $fallback) {
        return $fallback
    }

    throw 'pnpm is required. Install pnpm 10.15.0, for example: npm install -g pnpm@10.15.0'
}

Set-Location $repoRoot

Invoke-Step 'Dashboard desktop source contract' {
    powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1 -SkipBuild
    if ($LASTEXITCODE -ne 0) {
        throw "dashboard source contract check failed with exit code $LASTEXITCODE"
    }
}

if (-not $SkipDotnetBuild) {
    Invoke-Step ".NET build ($Configuration)" {
        dotnet build .\Dashboard.csproj -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) {
            throw ".NET build failed with exit code $LASTEXITCODE"
        }
    }
}

if (-not $SkipDotnetTests) {
    Invoke-Step ".NET tests ($Configuration)" {
        dotnet test .\tests\Dashboard.Tests\Dashboard.Tests.csproj -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) {
            throw ".NET tests failed with exit code $LASTEXITCODE"
        }
    }
}

if (-not $SkipFrontendTypeCheck -or -not $SkipFrontendTests -or -not $SkipFrontendBuild) {
    Invoke-Step 'Frontend dependencies' {
        $pnpmPath = Get-PnpmPath
        New-Item -ItemType Directory -Force -Path $pnpmStoreDir | Out-Null
        Push-Location $dashboardRoot
        try {
            & $pnpmPath install --frozen-lockfile --store-dir $pnpmStoreDir
            if ($LASTEXITCODE -ne 0) {
                throw "pnpm install failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }
}

if (-not $SkipFrontendTests) {
    Invoke-Step 'Frontend unit tests' {
        $pnpmPath = Get-PnpmPath
        Push-Location $dashboardRoot
        try {
            & $pnpmPath exec vitest run
            if ($LASTEXITCODE -ne 0) {
                throw "frontend unit tests failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }
}

if (-not $SkipFrontendTypeCheck) {
    Invoke-Step 'Frontend type-check' {
        $pnpmPath = Get-PnpmPath
        Push-Location $dashboardRoot
        try {
            & $pnpmPath exec vue-tsc --build --force
            if ($LASTEXITCODE -ne 0) {
                throw "frontend type-check failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }
}

if (-not $SkipFrontendBuild) {
    Invoke-Step 'Frontend build' {
        $pnpmPath = Get-PnpmPath
        Push-Location $dashboardRoot
        try {
            $previousFont = $env:FONT
            $previousDesktopBuild = $env:DESKTOP_BUILD
            $env:FONT = 'misans'
            $env:DESKTOP_BUILD = '1'
            & $pnpmPath exec vite build
            if ($LASTEXITCODE -ne 0) {
                throw "frontend build failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            $env:FONT = $previousFont
            $env:DESKTOP_BUILD = $previousDesktopBuild
            Pop-Location
        }
    }
}

Write-Host 'All checks completed.' -ForegroundColor Green
