# Runs without Pester, .NET builds, network, elevation, or user settings.
# External commands are faked; actual build/release scripts run in a temporary copy.
param()
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$originalLocation = Get-Location
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('Dashboard.BuildTests.' + [Guid]::NewGuid().ToString('N'))
$script:passed = 0

function Assert-True([bool]$Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

function New-Fixture([string]$Name, [bool]$WithAssets = $true) {
    $root = Join-Path $tempRoot $Name
    New-Item -ItemType Directory -Force (Join-Path $root 'tools') | Out-Null
    Copy-Item (Join-Path $sourceRoot 'build.ps1'), (Join-Path $sourceRoot 'create-release.ps1') $root
    Copy-Item (Join-Path $sourceRoot 'tools\assert-dashboard-assets.ps1') (Join-Path $root 'tools')
    if ($WithAssets) {
        New-Item -ItemType Directory -Force (Join-Path $root 'resources\dashboard\assets') | Out-Null
        [IO.File]::WriteAllText((Join-Path $root 'resources\dashboard\index.html'), '<html><script src="./assets/app.js"></script></html>')
        [IO.File]::WriteAllText((Join-Path $root 'resources\dashboard\assets\app.js'), 'test')
    }
    $global:DashboardBuildTestState = @{ RestoreExit = 0; PublishExit = 0; ChildExit = 0; Calls = @() }
    $global:LASTEXITCODE = 0
    return $root
}

function dotnet {
    $state = $global:DashboardBuildTestState
    $state.Calls += $args[0]
    if ($args[0] -eq 'restore') { $global:LASTEXITCODE = $state.RestoreExit; return }
    if ($args[0] -ne 'publish') { throw 'Unexpected dotnet call' }
    $global:LASTEXITCODE = $state.PublishExit
    if ($state.PublishExit -ne 0) { return }
    $output = $args[[Array]::IndexOf($args, '-o') + 1]
    New-Item -ItemType Directory -Force (Join-Path $output 'resources') | Out-Null
    [IO.File]::WriteAllText((Join-Path $output 'Dashboard.exe'), 'fake binary')
    Copy-Item 'resources\dashboard' (Join-Path $output 'resources') -Recurse
}

function powershell { $global:LASTEXITCODE = $global:DashboardBuildTestState.ChildExit }

function Expect-Failure([scriptblock]$Action, [string]$Message) {
    $caught = $null
    try { & $Action } catch { $caught = $_.Exception.Message }
    Assert-True ($null -ne $caught -and $caught.Contains($Message)) "Expected '$Message', got '$caught'"
    $script:passed++
}

try {
    $root = New-Fixture 'missing-ui' $false
    Expect-Failure { & (Join-Path $root 'build.ps1') -SkipDashboardBuild } 'Dashboard UI is missing'
    Assert-True ($global:DashboardBuildTestState.Calls.Count -eq 0) 'Missing UI must fail before dotnet'

    $root = New-Fixture 'missing-reference'
    Remove-Item (Join-Path $root 'resources\dashboard\assets\app.js')
    Expect-Failure { & (Join-Path $root 'build.ps1') -SkipDashboardBuild } 'missing file'

    $root = New-Fixture 'traversal'
    [IO.File]::WriteAllText((Join-Path $root 'resources\dashboard\index.html'), '<script src="../outside.js"></script>')
    Expect-Failure { & (Join-Path $root 'build.ps1') -SkipDashboardBuild } 'escapes'

    $root = New-Fixture 'restore-failure'
    $global:DashboardBuildTestState.RestoreExit = 9
    Expect-Failure { & (Join-Path $root 'build.ps1') -SkipDashboardBuild } 'dotnet restore failed'
    Assert-True ($global:DashboardBuildTestState.Calls -notcontains 'publish') 'Publish must not run after failed restore'

    $root = New-Fixture 'publish-failure'
    $global:DashboardBuildTestState.PublishExit = 8
    Expect-Failure { & (Join-Path $root 'build.ps1') -SkipDashboardBuild } 'dotnet publish failed'

    $root = New-Fixture 'ui-build-failure'
    $global:DashboardBuildTestState.ChildExit = 7
    Expect-Failure { & (Join-Path $root 'build.ps1') } 'dashboard UI build failed'
    Assert-True ($global:DashboardBuildTestState.Calls.Count -eq 0) 'UI failure must stop dotnet'

    $root = New-Fixture 'release-failure'
    $publish = Join-Path $root 'artifacts\publish\Dashboard-Release-win-x64'
    New-Item -ItemType Directory -Force $publish | Out-Null
    [IO.File]::WriteAllText((Join-Path $publish 'Dashboard.exe'), 'stale binary')
    $global:DashboardBuildTestState.ChildExit = 6
    Expect-Failure { & (Join-Path $root 'create-release.ps1') -SkipDashboardBuild -OutputZip 'test.zip' } 'packaging cancelled'
    Assert-True (-not (Test-Path (Join-Path $root 'artifacts\releases\test.zip'))) 'Failed child build must not package stale files'

    $root = New-Fixture 'successful-build'
    $publish = Join-Path $root 'artifacts\publish\Dashboard-Release-win-x64'
    New-Item -ItemType Directory -Force $publish | Out-Null
    [IO.File]::WriteAllText((Join-Path $publish 'stale.txt'), 'stale')
    & (Join-Path $root 'build.ps1') -SkipDashboardBuild
    Assert-True (-not (Test-Path (Join-Path $publish 'stale.txt'))) 'Successful build must clear old output'
    Assert-True (Test-Path (Join-Path $publish 'resources\dashboard\assets\app.js')) 'Published UI missing'
    & (Join-Path $root 'create-release.ps1') -SkipDashboardBuild -OutputZip 'test.zip'
    Assert-True (Test-Path (Join-Path $root 'artifacts\releases\test.zip')) 'Valid output should be packaged'
    $script:passed++

    $root = New-Fixture 'incomplete-publish'
    New-Item -ItemType Directory -Force (Join-Path $root 'artifacts\publish\Dashboard-Release-win-x64') | Out-Null
    Expect-Failure { & (Join-Path $root 'create-release.ps1') -SkipDashboardBuild } 'Dashboard.exe is missing'

    Write-Host "Build/release script tests passed: $script:passed"
}
finally {
    Remove-Variable DashboardBuildTestState -Scope Global -ErrorAction SilentlyContinue
    Set-Location $originalLocation
    if (Test-Path $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
