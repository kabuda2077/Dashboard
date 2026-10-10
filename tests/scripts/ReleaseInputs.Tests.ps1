#requires -Version 7.0
# Run the production release entry with offline build stages, real snapshots and ZIP replacement.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestPaths.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $repository 'tools/internal/source-fingerprint.ps1')
$fixture = New-TemporaryTestDirectory 'release-inputs'
$engine = (Get-Process -Id $PID).Path
$gitExecutable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Put([string]$Path, [string]$Text) {
    $file = Join-Path $fixture $Path
    $null = New-Item -ItemType Directory -Force (Split-Path $file)
    [IO.File]::WriteAllText($file, $Text, [Text.UTF8Encoding]::new($false))
}
function Invoke-FixtureGit([string[]]$Arguments) {
    $result = @(& $gitExecutable -C $fixture @Arguments)
    if ($LASTEXITCODE) { throw "Fixture git failed: $($Arguments -join ' ')" }
    return $result
}
try {
    Put '.gitignore' ".tmp/`nartifacts/`nresources/dashboard/`n"
    Put 'Dashboard.csproj' '<Project><PropertyGroup><Version>2.0.0</Version></PropertyGroup></Project>'
    Put 'dashboard-src/src/Page.vue' 'original source'
    Put 'dashboard-src/public/favicon.ico' 'tracked icon source'
    Put 'resources/app.ico' 'old generated app icon'
    Put 'resources/tray.ico' 'old generated tray icon'
    # Keep the production entry's normal tools/internal relative import.
    $null = New-Item -ItemType Directory -Force (Join-Path $fixture 'tools/internal')
    Copy-Item (Join-Path $repository 'tools/create-release.ps1') (Join-Path $fixture 'tools/create-release.ps1')
    $pipeline = @'
. '__PIPELINE__'
$script:RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$script:Stage = [IO.File]::ReadAllText((Join-Path $script:RepositoryRoot '.tmp/stage'))
function Invoke-Verification {
    [IO.File]::WriteAllText((Join-Path $script:RepositoryRoot 'resources/app.ico'), 'rebuilt app icon')
    [IO.File]::WriteAllText((Join-Path $script:RepositoryRoot 'resources/tray.ico'), 'rebuilt tray icon')
    if ($script:Stage -eq 'verification') {
        [IO.File]::WriteAllText((Join-Path $script:RepositoryRoot 'dashboard-src/src/Page.vue'), 'changed after verification')
    }
    if ($script:Stage -eq 'revision') {
        git -C $script:RepositoryRoot commit --quiet --allow-empty -m 'source revision changed'
        if ($LASTEXITCODE) { throw 'Fixture commit failed.' }
    }
}
function Publish-Dashboard {
    $output = Join-Path $script:RepositoryRoot '.tmp/publish'
    $null = New-Item -ItemType Directory -Force (Join-Path $output 'resources/dashboard/assets')
    [IO.File]::WriteAllText((Join-Path $output 'Dashboard.exe'), 'fixture executable')
    [IO.File]::WriteAllText((Join-Path $output 'resources/dashboard/index.html'), '<script src="./assets/app.js"></script>')
    [IO.File]::WriteAllText((Join-Path $output 'resources/dashboard/assets/app.js'), 'fixture UI')
    if ($script:Stage -eq 'publish') { Remove-Item (Join-Path $script:RepositoryRoot 'dashboard-src/src/Page.vue') }
    return $output
}
$script:ArchiveAssertion = ${function:Assert-ReleaseArchive}
function Assert-ReleaseArchive {
    param([string]$ArchivePath, [string]$PublishDirectory)
    & $script:ArchiveAssertion -ArchivePath $ArchivePath -PublishDirectory $PublishDirectory
    if ($script:Stage -eq 'archive') {
        [IO.File]::WriteAllText((Join-Path $script:RepositoryRoot 'dashboard-src/src/New.vue'), 'new input while writing ZIP')
    }
}
'@
    Put 'tools/internal/pipeline.ps1' ($pipeline.Replace('__PIPELINE__', (Join-Path $repository 'tools/internal/pipeline.ps1').Replace("'", "''")))
    $null = Invoke-FixtureGit @('init', '--quiet')
    $null = Invoke-FixtureGit @('config', 'user.email', 'release-inputs@example.test')
    $null = Invoke-FixtureGit @('config', 'user.name', 'Release inputs fixture')
    $null = Invoke-FixtureGit @('config', 'core.autocrlf', 'false')
    $null = Invoke-FixtureGit @('add', '.')
    $null = Invoke-FixtureGit @('commit', '--quiet', '-m', 'release source')
    Put 'dashboard-src/src/Untracked.vue' 'existing untracked source'
    Put '.tmp/stage' 'success'
    $starting = Get-ReleaseSourceSnapshot -Root $fixture
    $output = @(& $engine -NoProfile -File (Join-Path $fixture 'tools/create-release.ps1') 2>&1)
    Assert-True ($LASTEXITCODE -eq 0) ($output -join "`n")
    $zip = Join-Path $fixture 'artifacts/releases/Dashboard-v2.0.0-win-x64.zip'
    $evidence = Join-Path $fixture 'artifacts/verification/Dashboard-v2.0.0-win-x64'
    $manifest = [IO.File]::ReadAllLines((Join-Path $evidence 'inputs.tsv'))
    $record = [IO.File]::ReadAllText((Join-Path $evidence 'verification.json')) | ConvertFrom-Json
    Assert-True (($manifest -join "`n") -ceq ($starting.manifest -join "`n")) 'Release did not save the starting input manifest.'
    Assert-True ($record.sourceHead -ceq $starting.head -and $record.uncommittedChanges) 'Starting revision or dirty source was lost.'
    Assert-True (@($manifest | Where-Object { $_ -like 'dashboard-src/src/Untracked.vue*' }).Count -eq 1) 'Existing untracked input was omitted.'
    Assert-True (@($manifest | Where-Object { $_ -match '^resources/(app|tray)\.ico\t' }).Count -eq 0) 'Derived icons were treated as source inputs.'
    Assert-ReleaseSourceSnapshot -Root $fixture -Snapshot $starting
    $zipHash = (Get-FileHash $zip).Hash
    $recordHash = (Get-FileHash (Join-Path $evidence 'verification.json')).Hash
    foreach ($stage in @('verification', 'publish', 'archive', 'revision')) {
        Put 'dashboard-src/src/Page.vue' 'original source'
        Remove-Item (Join-Path $fixture 'dashboard-src/src/New.vue') -ErrorAction SilentlyContinue
        Put '.tmp/stage' $stage
        $output = @(& $engine -NoProfile -File (Join-Path $fixture 'tools/create-release.ps1') 2>&1)
        Assert-True ($LASTEXITCODE -ne 0 -and ($output -join "`n") -like '*Release source inputs changed*') "Mutation during $stage was accepted: $($output -join "`n")"
        Assert-True ((Get-FileHash $zip).Hash -ceq $zipHash) "Mutation during $stage replaced the previous ZIP."
        Assert-True ((Get-FileHash (Join-Path $evidence 'verification.json')).Hash -ceq $recordHash) "Mutation during $stage replaced the previous verification record."
        Assert-True (@(Get-ChildItem (Split-Path $zip) -Filter '*.tmp').Count -eq 0) 'Rejected temporary ZIP was left behind.'
    }
    Write-Host 'Release starting inputs, derived icons, mutations and old ZIP preservation passed.'
}
finally {
    Get-ChildItem -LiteralPath $fixture -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
    Remove-TemporaryTestDirectory $fixture
}
