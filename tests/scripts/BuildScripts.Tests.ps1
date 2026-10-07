#requires -Version 7.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestPaths.ps1')
. (Join-Path $PSScriptRoot '..\..\tools\internal\pipeline.ps1')
Initialize-FrontendTools
$script:calls = [Collections.Generic.List[string]]::new()
function Test-DesktopSources { $script:calls.Add('sources') }
function Prepare-Frontend { $script:calls.Add('install') }
function Test-FrontendTypes { $script:calls.Add('types') }
function Build-Frontend { $script:calls.Add('ui') }
function Test-Host { param($Configuration, $IncludeWebViewIntegration) $script:calls.Add("host:$Configuration") }
function Test-Frontend { $script:calls.Add('frontend') }
function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message } }

Invoke-Verification -Configuration Release 6>$null
Assert-True (($script:calls -join ',') -eq 'sources,install,types,ui,host:Release,frontend') 'Verification order or step count changed.'
$script:calls.Clear()
function Test-FrontendTypes { $script:calls.Add('types'); throw 'expected type failure' }
$failed = $false
try { Invoke-Verification -Configuration Release } catch { $failed = $_.Exception.Message -like '*expected type failure*' }
Assert-True $failed 'An earlier failure must stop the pipeline.'
Assert-True (($script:calls -join ',') -eq 'sources,install,types') 'Build/test work ran after an earlier failure.'
$failed = $false
try { Invoke-Checked 'expected native failure' { $global:LASTEXITCODE = 23 } } catch { $failed = $_.Exception.Message -like '*exit 23*' }
$global:LASTEXITCODE = 0
Assert-True $failed 'Native non-zero exit was not propagated.'
$failed = $false
try { Invoke-Checked 'expected missing executable' { & 'dashboard-no-such-command-for-test' } 2>$null } catch { $failed = $true }
Assert-True $failed 'A missing executable must not be treated as a successful step.'

$temp = New-TemporaryTestDirectory 'build-scripts'
$script:RepositoryRoot = $temp
$publish = Join-Path $temp 'publish'
New-Item -ItemType Directory -Force (Join-Path $publish 'resources\dashboard\assets') | Out-Null
try {
    [IO.File]::WriteAllText((Join-Path $publish 'Dashboard.exe'), 'fixture, not an executable')
    [IO.File]::WriteAllText((Join-Path $publish 'resources\dashboard\index.html'), '<script src="./assets/app.js"></script>')
    [IO.File]::WriteAllText((Join-Path $publish 'resources\dashboard\assets\app.js'), 'fixture')
    $zip = Write-ReleaseArchive $publish 'test.zip'
    Assert-True (Test-Path $zip) 'Verified fixture archive was not produced.'
    [IO.File]::WriteAllText((Join-Path $publish 'resources\dashboard\assets\app.js'), 'updated fixture')
    $originalHash = (Get-FileHash $zip).Hash
    $zip = Write-ReleaseArchive $publish 'test.zip'
    Assert-True ((Get-FileHash $zip).Hash -ne $originalHash) 'An existing release could not be replaced with verified new content.'
    $before = (Get-FileHash $zip).Hash
    $tampered = Join-Path $temp 'tampered.zip'
    Copy-Item $zip $tampered
    $archive = [IO.Compression.ZipFile]::Open($tampered, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $archive.GetEntry('resources/dashboard/assets/app.js').Delete()
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('resources/dashboard/assets/app.js').Open())
        try { $writer.Write('changed bytes, same entry name and count') } finally { $writer.Dispose() }
    } finally { $archive.Dispose() }
    $failed = $false
    try { Assert-ReleaseArchive -ArchivePath $tampered -PublishDirectory $publish } catch { $failed = $_.Exception.Message -like '*ZIP content does not match*' }
    Assert-True $failed 'A ZIP with correct names but changed content was accepted.'
    [IO.File]::WriteAllText((Join-Path $publish 'settings.json'), 'must not be packaged')
    $failed = $false
    try { Write-ReleaseArchive $publish 'test.zip' | Out-Null } catch { $failed = $_.Exception.Message -like '*leaked*' }
    Assert-True $failed 'User settings were allowed into the release.'
    Assert-True ((Get-FileHash $zip).Hash -eq $before) 'A failed candidate overwrote an existing archive.'
    Remove-Item (Join-Path $publish 'settings.json')
    foreach ($relative in @('profiles\config.yaml', 'profile\nested\user.json', 'cores\renamed.exe', 'config\user.json', 'mihomo.exe', 'sing-box.exe', 'mihomo-windows-amd64.exe', 'config.json', 'config.test.json', 'custom.yml', 'webview-data-v2\user.db', 'icon-cache\cached.png', 'backups\state.json', '.hidden\settings.json')) {
        $file = Join-Path $publish $relative
        New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
        [IO.File]::WriteAllText($file, 'must not be packaged')
        $failed = $false
        try { Write-ReleaseArchive $publish 'test.zip' | Out-Null } catch { $failed = $_.Exception.Message -like '*leaked*' }
        Assert-True $failed "Forbidden release file was accepted: $relative"
        Assert-True ((Get-FileHash $zip).Hash -eq $before) 'Rejected candidate replaced an existing archive.'
        Remove-Item -LiteralPath $file
    }
    Remove-Item (Join-Path $publish 'resources\dashboard\assets\app.js')
    $failed = $false
    try { Assert-PublishDirectory $publish } catch { $failed = $true }
    Assert-True $failed 'Missing entry dependency was not detected.'
    $failed = $false
    try { Write-ReleaseArchive $publish '../escape.zip' | Out-Null } catch { $failed = $true }
    Assert-True $failed 'An archive path escaped the release directory.'
}
finally { Remove-TemporaryTestDirectory $temp }
# Verify PowerShell 7's native output handling with actual process redirection.
# Warnings must remain visible; a failing exit must still stop the pipeline.
$stderrFixture = Join-Path $PSScriptRoot 'native-stderr.cjs'
$engine = (Get-Process -Id $PID).Path
$probe = New-TemporaryTestDirectory 'native-stderr'
try {
    $wrapper = Join-Path $probe 'probe.ps1'
    $pipelinePath = Join-Path $PSScriptRoot '..\..\tools\internal\pipeline.ps1'
    $code = ". '" + $pipelinePath.Replace("'", "''") + "'`n" +
        "`$PSNativeCommandUseErrorActionPreference = `$true`n" +
        "Invoke-Checked 'native stderr regression' { & '" + $script:FrontendNode.Replace("'", "''") + "' '" + $stderrFixture.Replace("'", "''") + "' }`n"
    [IO.File]::WriteAllText($wrapper, $code)
    $child = Start-Process -FilePath $engine -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wrapper) -NoNewWindow -RedirectStandardOutput (Join-Path $probe 'stdout.log') -RedirectStandardError (Join-Path $probe 'stderr.log') -PassThru
    $null = $child.Handle
    $child.WaitForExit()
    Assert-True ($child.ExitCode -eq 0) 'Successful multi-line native stderr was rejected under output redirection.'
    $stderr = [IO.File]::ReadAllText((Join-Path $probe 'stderr.log'))
    Assert-True ($stderr -match 'warning-one' -and $stderr -match 'warning-two') 'Native warnings disappeared.'
    $code = $code.Replace("' }", "' 19 }")
    [IO.File]::WriteAllText($wrapper, $code)
    $child = Start-Process -FilePath $engine -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wrapper) -NoNewWindow -RedirectStandardOutput (Join-Path $probe 'failed.stdout.log') -RedirectStandardError (Join-Path $probe 'failed.stderr.log') -PassThru
    $null = $child.Handle
    $child.WaitForExit()
    Assert-True ($child.ExitCode -ne 0) 'A native failure with multi-line stderr was ignored.'
    Assert-True ([IO.File]::ReadAllText((Join-Path $probe 'failed.stderr.log')) -match 'exit 19') 'Native exit code was not propagated.'
}
finally {
    try {
        $reports = Get-TestReportDirectory 'build-scripts'
        Get-ChildItem -LiteralPath $probe -Filter '*.log' -File | Copy-Item -Destination $reports -Force
    }
    finally { Remove-TemporaryTestDirectory $probe }
}
Write-Host 'Build/release pipeline regression tests passed.' -ForegroundColor Green
