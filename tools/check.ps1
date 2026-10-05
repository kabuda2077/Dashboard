#requires -Version 7.0
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$IncludeWebViewIntegration,
    [switch]$IncludeRealCoreIntegration,
    [switch]$IncludePerformanceIntegration,
    [switch]$IncludeSlowIntegration
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\pipeline.ps1')
try {
    Invoke-Verification -Configuration $Configuration -IncludeWebViewIntegration ([bool]$IncludeWebViewIntegration) -IncludeRealCoreIntegration ([bool]$IncludeRealCoreIntegration) -IncludePerformanceIntegration ([bool]$IncludePerformanceIntegration) -IncludeSlowIntegration ([bool]$IncludeSlowIntegration)
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
