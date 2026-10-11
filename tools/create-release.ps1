#requires -Version 7.0
param(
    [string]$OutputZip = '',
    [switch]$IncludeWebViewIntegration,
    [switch]$IncludeRealCoreIntegration,
    [switch]$IncludePerformanceIntegration
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\pipeline.ps1')
try {
    # A release invocation verifies its own inputs; it never trusts a previous check or a stale publish folder.
    $source = Get-ReleaseSourceSnapshot -Root $script:RepositoryRoot
    Invoke-Verification -Configuration Release -IncludeWebViewIntegration ([bool]$IncludeWebViewIntegration) -IncludeRealCoreIntegration ([bool]$IncludeRealCoreIntegration) -IncludePerformanceIntegration ([bool]$IncludePerformanceIntegration)
    Assert-ReleaseSourceSnapshot -Root $script:RepositoryRoot -Snapshot $source
    $publish = Publish-Dashboard -Configuration Release
    $project = [xml][IO.File]::ReadAllText((Join-Path $script:RepositoryRoot 'Dashboard.csproj'))
    $version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    if (-not $OutputZip) { $OutputZip = "Dashboard-v$version-win-x64.zip" }
    $archive = Write-ReleaseArchive -PublishDirectory $publish -OutputZip $OutputZip -SourceSnapshot $source
    $evidenceDirectory = Join-Path $script:RepositoryRoot ('artifacts\verification\' + [IO.Path]::GetFileNameWithoutExtension($archive))
    New-Item -ItemType Directory -Force $evidenceDirectory | Out-Null
    $manifestPath = Join-Path $evidenceDirectory 'inputs.tsv'
    [IO.File]::WriteAllLines($manifestPath, $source.manifest, [Text.UTF8Encoding]::new($false))
    $record = [ordered]@{
        version = $version; configuration = 'Release'; runtime = 'win-x64'
        sourceHead = $source.head; uncommittedChanges = $source.dirty
        inputsSha256 = (Get-FileHash $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        zipSha256 = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        automatedChecks = 'passed'; webViewIntegration = [bool]$IncludeWebViewIntegration
        realCoreIntegration = [bool]$IncludeRealCoreIntegration
        performanceIntegration = [bool]$IncludePerformanceIntegration
        manualSystemAcceptance = 'not implied by this command'
        schemaVersion = 2; oldSettingsMigration = $false
        webViewResetPolicy = 'application-version'
        webViewBrowserDataDirectory = 'resources/webview-data-v2/EBWebView'
    }
    $record | ConvertTo-Json | Out-File (Join-Path $evidenceDirectory 'verification.json') -Encoding utf8
    Write-Host "Verified release candidate: $archive" -ForegroundColor Green
    Write-Host "Verification records: $evidenceDirectory"
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
