#requires -Version 7.0
# Repository/tooling checks belong in CI or tooling/document changes, not every application build.
$ErrorActionPreference = 'Stop'
try {
    & (Join-Path $PSScriptRoot '..\tests\scripts\BuildScripts.Tests.ps1')
    & (Join-Path $PSScriptRoot '..\tests\scripts\DevelopmentPaths.Tests.ps1')
    & (Join-Path $PSScriptRoot '..\tests\scripts\Documentation.Tests.ps1')
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
