param(
    [string]$Configuration = 'Debug',
    [switch]$SkipDotnetBuild,
    [switch]$SkipDotnetTests,
    [switch]$SkipFrontendTypeCheck,
    [switch]$SkipFrontendTests,
    [switch]$SkipFrontendBuild
)

$ErrorActionPreference = 'Stop'

# Several tools here write harmless warnings to stderr while exiting 0 (vite's
# [PLUGIN_TIMINGS] hint, pnpm progress, git's safe.directory notice). Under
# Windows PowerShell 5.1 those lines become NativeCommandError ErrorRecords as
# soon as they enter the success pipeline, and $ErrorActionPreference = 'Stop'
# then aborts the whole gate on a warning. Do NOT redirect native stderr with
# 2>&1 here: the redirect is what feeds those records into the pipeline. Instead
# Invoke-Step drops to 'Continue' around each step, so native stderr stays
# informational and every step is judged by $LASTEXITCODE alone.

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dashboardRoot = Join-Path $repoRoot 'dashboard-src'
$pnpmStoreDir = Join-Path $repoRoot '.tmp\pnpm-store'

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Script
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    # Local to this scope only; the caller keeps 'Stop'. Steps must fail loudly
    # via an explicit throw or a $LASTEXITCODE check, never via native stderr.
    $ErrorActionPreference = 'Continue'
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

    throw 'pnpm is required. Install pnpm 11.20.0, for example: npm install -g pnpm@11.20.0'
}

function Invoke-Pnpm {
    param(
        [Parameter(Mandatory = $true)][string]$PnpmPath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    $previousCi = $env:CI
    try {
        $env:CI = 'true'
        $env:PNPM_STORE_DIR = $pnpmStoreDir
        $env:npm_config_store_dir = $pnpmStoreDir
        & $PnpmPath @Arguments
    }
    finally {
        $env:CI = $previousCi
    }
}

Set-Location $repoRoot

Invoke-Step 'Dashboard desktop source contract' {
    powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1 -SkipBuild
    if ($LASTEXITCODE -ne 0) {
        throw "dashboard source contract check failed with exit code $LASTEXITCODE"
    }
}

Invoke-Step 'Build/release script regression tests' {
    powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\scripts\BuildScripts.Tests.ps1
    if ($LASTEXITCODE -ne 0) {
        throw "build/release script tests failed with exit code $LASTEXITCODE"
    }
}

if (-not $SkipFrontendTypeCheck -or -not $SkipFrontendTests -or -not $SkipFrontendBuild) {
    Invoke-Step 'Frontend dependencies' {
        $pnpmPath = Get-PnpmPath
        New-Item -ItemType Directory -Force -Path $pnpmStoreDir | Out-Null
        Push-Location $dashboardRoot
        try {
            Invoke-Pnpm $pnpmPath install --frozen-lockfile --store-dir $pnpmStoreDir
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
        Push-Location $dashboardRoot
        try {
            & .\node_modules\.bin\vitest.cmd run
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
        Push-Location $dashboardRoot
        try {
            & .\node_modules\.bin\vue-tsc.cmd --build --force
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
    # Runs the full build-zashboard.ps1 (no -SkipBuild): vite build, then the copy
    # into resources\dashboard\ and the icon sync. Dashboard.csproj includes
    # resources\dashboard\**\* as Content, and those files are not tracked in git,
    # so the .NET build below depends on this step having produced them.
    Invoke-Step 'Dashboard UI build' {
        # That script runs its own pnpm install. Without CI, pnpm asks before
        # purging node_modules and aborts with ERR_PNPM_ABORTED_REMOVE_MODULES_DIR_NO_TTY
        # because this gate has no TTY. Invoke-Pnpm sets CI for the calls it owns;
        # the child process needs it too.
        $previousCi = $env:CI
        try {
            $env:CI = 'true'
            powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1
            if ($LASTEXITCODE -ne 0) {
                throw "dashboard UI build failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            $env:CI = $previousCi
        }
    }
}

if ($SkipFrontendBuild -and (-not $SkipDotnetBuild -or -not $SkipDotnetTests)) {
    # Skipping the UI build is only safe when a previous build left the assets in
    # place. Fail with the real reason instead of shipping a UI-less binary.
    & (Join-Path $repoRoot 'tools\assert-dashboard-assets.ps1') -Path (Join-Path $repoRoot 'resources\dashboard')
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

Write-Host 'All checks completed.' -ForegroundColor Green
