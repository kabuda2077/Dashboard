#requires -Version 7.0
param(
    [string]$OutputZip = '',
    [switch]$IncludeWebViewIntegration,
    [switch]$IncludeRealCoreIntegration,
    [switch]$IncludePerformanceIntegration,
    [switch]$IncludeSlowIntegration
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'internal\pipeline.ps1')
try {
    # A release invocation verifies its own inputs; it never trusts a previous check or a stale publish folder.
    Invoke-Verification -Configuration Release -IncludeWebViewIntegration ([bool]$IncludeWebViewIntegration) -IncludeRealCoreIntegration ([bool]$IncludeRealCoreIntegration) -IncludePerformanceIntegration ([bool]$IncludePerformanceIntegration) -IncludeSlowIntegration ([bool]$IncludeSlowIntegration)
    $publish = Publish-Dashboard -Configuration Release
    $project = [xml][IO.File]::ReadAllText((Join-Path $script:RepositoryRoot 'Dashboard.csproj'))
    $version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
    if (-not $OutputZip) { $OutputZip = "Dashboard-v$version-win-x64.zip" }
    $archive = Write-ReleaseArchive -PublishDirectory $publish -OutputZip $OutputZip
    $manifest = @()
    Push-Location $script:RepositoryRoot
    try {
        $head = git rev-parse HEAD
        $dirty = @(git status --porcelain).Count -gt 0
        $files = @(git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
        foreach ($file in $files) {
            $hash = if (Test-Path -LiteralPath $file -PathType Leaf) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'MISSING' }
            $manifest += "$file`t$hash"
        }
    }
    finally { Pop-Location }
    $evidenceDirectory = Join-Path $script:RepositoryRoot ('artifacts\verification\' + [IO.Path]::GetFileNameWithoutExtension($archive))
    New-Item -ItemType Directory -Force $evidenceDirectory | Out-Null
    $manifestPath = Join-Path $evidenceDirectory 'inputs.tsv'
    [IO.File]::WriteAllLines($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
    $record = [ordered]@{
        version = $version; configuration = 'Release'; runtime = 'win-x64'
        sourceHead = $head; uncommittedChanges = $dirty
        inputsSha256 = (Get-FileHash $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        zipSha256 = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        automatedChecks = 'passed'; webViewIntegration = [bool]$IncludeWebViewIntegration
        realCoreIntegration = [bool]$IncludeRealCoreIntegration
        performanceIntegration = [bool]$IncludePerformanceIntegration
        slowIntegration = [bool]$IncludeSlowIntegration
        manualSystemAcceptance = 'not implied by this command'
        schemaVersion = 2; oldSettingsMigration = $false
    }
    $record | ConvertTo-Json | Out-File (Join-Path $evidenceDirectory 'verification.json') -Encoding utf8
    Write-Host "Verified release candidate: $archive" -ForegroundColor Green
    Write-Host "Verification records: $evidenceDirectory"
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
