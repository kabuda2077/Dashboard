#requires -Version 7.0
param(
    [ValidateSet('Application', 'Maintenance', 'All')][string]$Scope = 'Application',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$IncludeWebViewIntegration,
    [switch]$IncludeRealCoreIntegration,
    [switch]$IncludePerformanceIntegration
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\pipeline.ps1')
try {
    if ($Scope -ne 'Application') {
        foreach ($test in @('BuildScripts', 'ReleaseInputs', 'DevelopmentPaths', 'Documentation', 'UpstreamAudit')) {
            # These scripts assert expected native failures; their final LASTEXITCODE is not a test result.
            & (Join-Path $PSScriptRoot "../tests/scripts/$test.Tests.ps1")
            if (-not $?) { throw "$test maintenance checks failed." }
            $global:LASTEXITCODE = 0
        }
    }
    if ($Scope -ne 'Maintenance') {
        Invoke-Verification -Configuration $Configuration -IncludeWebViewIntegration ([bool]$IncludeWebViewIntegration) -IncludeRealCoreIntegration ([bool]$IncludeRealCoreIntegration) -IncludePerformanceIntegration ([bool]$IncludePerformanceIntegration)
    }
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
