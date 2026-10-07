#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestPaths.ps1')
. (Join-Path $PSScriptRoot '..\..\tools\internal\pipeline.ps1')
function Assert-PathRule([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }

$root = $script:RepositoryRoot
$systemTempPattern = 'Get' + 'TempPath|\$env:(?:TEMP|TMP)\b'
$testSources = @(Get-ChildItem (Join-Path $root 'tests\Dashboard.Tests') -File -Filter '*.cs') +
    @(Get-ChildItem $PSScriptRoot -File -Filter '*.ps1')
foreach ($file in $testSources) {
    if ([IO.File]::ReadAllText($file.FullName) -match $systemTempPattern) {
        throw "Tests must use the shared project-local temporary-directory helper: $($file.FullName)"
    }
}
foreach ($relative in @('tools/internal/pipeline.ps1', 'tools/internal/frontend-tools.ps1')) {
    $source = [IO.File]::ReadAllText((Join-Path $root $relative))
    Assert-PathRule ($source -notmatch '--store-dir|\$env:(?:PNPM_STORE_DIR|npm_config_store_dir)\s*=') "Tooling overrides pnpm store configuration: $relative"
}

$first = New-TemporaryTestDirectory 'ownership'
$second = New-TemporaryTestDirectory 'ownership'
try {
    Assert-PathRule ($first -ne $second) 'Temporary directories must be unique.'
    Assert-PathRule ($first.StartsWith((Join-Path $root '.tmp\tests') + [IO.Path]::DirectorySeparatorChar)) 'Test directory is not project-local.'
    $rejected = $false
    try { Remove-TemporaryTestDirectory $root } catch { $rejected = $_.Exception.Message -like '*unowned*' }
    Assert-PathRule $rejected 'Cleanup accepted the repository root.'
}
finally { Remove-TemporaryTestDirectory $first; Remove-TemporaryTestDirectory $second }

# A project-local validation copy or archived source must not enter the app's compile glob.
$compileProbe = New-TemporaryTestDirectory 'compile-boundary'
$archiveProbe = Join-Path $root ('artifacts\archive\compile-boundary-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $archiveProbe -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $compileProbe 'Poison.cs'), '#error temporary source must not compile')
    [IO.File]::WriteAllText((Join-Path $archiveProbe 'Poison.cs'), '#error archived source must not compile')
    $query = & dotnet.exe msbuild (Join-Path $root 'Dashboard.csproj') -nologo -getItem:Compile
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect application compile items.' }
    $items = @(($query -join [Environment]::NewLine | ConvertFrom-Json).Items.Compile)
    Assert-PathRule ($items.Count -gt 0) 'The application has no compile inputs.'
    $sourcePrefix = (Join-Path $root 'src') + [IO.Path]::DirectorySeparatorChar
    foreach ($item in $items) {
        Assert-PathRule ($item.FullPath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) "Non-source input reached application compilation: $($item.FullPath)"
    }
}
finally { Remove-TemporaryTestDirectory $compileProbe; Remove-TestTreeWithoutFollowingLinks $archiveProbe }

$fixture = New-TemporaryTestDirectory 'frontend-tools'
try {
    New-Item -ItemType Directory -Path (Join-Path $fixture 'dashboard-src') | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'dashboard-src\package.json'), '{"packageManager":"pnpm@11.20.0"}')
    [IO.File]::WriteAllText((Join-Path $fixture '.node-version'), '24.21.0')
    [IO.File]::WriteAllText((Join-Path $fixture 'node-global.ps1'), "`$global:LASTEXITCODE=0; Write-Output 'v22.23.3'")
    [IO.File]::WriteAllText((Join-Path $fixture 'node-managed.ps1'), "`$global:LASTEXITCODE=0; Write-Output 'v24.21.0'")
    [IO.File]::WriteAllText((Join-Path $fixture 'pnpm.ps1'), @'
$global:LASTEXITCODE = 0
if ((Get-Location).Path -ne (Join-Path $PSScriptRoot 'dashboard-src')) { throw 'pnpm was invoked outside the frontend directory' }
if ($args -contains '--version') { Write-Output '11.20.0'; return }
if ($args -contains 'dlx') { Write-Output (Join-Path $PSScriptRoot 'node-managed.ps1'); return }
if ($args -contains 'store') { Write-Output (Join-Path $PSScriptRoot 'configured-store'); return }
if ($args -contains 'install') {
    $record = [ordered]@{ arguments=$args; directory=(Get-Location).Path; path=$env:PATH; pnpmStore=$env:PNPM_STORE_DIR; npmStore=$env:npm_config_store_dir }
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'invocation.json'), ($record | ConvertTo-Json))
    return
}
throw 'Unexpected pnpm invocation'
'@)
    $script:FixtureRoot = $fixture
    function Get-Command {
        [CmdletBinding()] param([string]$Name)
        if ($Name -eq 'pnpm.cmd') { return [pscustomobject]@{Source=(Join-Path $script:FixtureRoot 'pnpm.ps1')} }
        if ($Name -eq 'node.exe') { return [pscustomobject]@{Source=(Join-Path $script:FixtureRoot 'node-global.ps1')} }
        throw "Unexpected tool lookup: $Name"
    }
    $script:RepositoryRoot = $fixture
    $beforePath = $env:PATH; $beforeCi = $env:CI
    $beforePnpmStore = $env:PNPM_STORE_DIR; $beforeNpmStore = $env:npm_config_store_dir
    Prepare-Frontend
    $record = [IO.File]::ReadAllText((Join-Path $fixture 'invocation.json')) | ConvertFrom-Json
    Assert-PathRule (($record.arguments -join ' ') -eq 'install --frozen-lockfile') 'Install arguments no longer preserve the lockfile or override store configuration.'
    Assert-PathRule ($record.directory -eq (Join-Path $fixture 'dashboard-src')) 'Install did not use the frontend directory.'
    Assert-PathRule ($script:FrontendNode -eq (Join-Path $fixture 'node-managed.ps1')) 'Managed Node was not selected when the ambient version differed.'
    Assert-PathRule ($record.path.StartsWith($fixture + [IO.Path]::PathSeparator)) 'Lifecycle scripts did not inherit the selected Node path.'
    Assert-PathRule ([string]$record.pnpmStore -eq [string]$beforePnpmStore -and [string]$record.npmStore -eq [string]$beforeNpmStore) 'Install changed store environment settings.'
    Assert-PathRule ($env:PATH -eq $beforePath -and [string]$env:CI -eq [string]$beforeCi) 'Tool selection leaked process environment changes.'
}
finally { Remove-TemporaryTestDirectory $fixture }
Write-Host 'Project-local test paths and frontend tool/configuration selection passed.' -ForegroundColor Green
