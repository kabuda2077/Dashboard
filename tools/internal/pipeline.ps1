#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$script:RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$script:PipelineDirectory = $PSScriptRoot

function Invoke-Checked {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "==> $Name" -ForegroundColor Cyan
    # Native stderr remains diagnostic output. Exit codes decide native success;
    # PowerShell invocation errors still terminate, including missing commands.
    $ErrorActionPreference = 'Stop'
    $PSNativeCommandUseErrorActionPreference = $false
    $global:LASTEXITCODE = 0
    & $Action | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
}

function Prepare-Frontend {
    $nodeVersion = [string](& node.exe --version)
    if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v24\.') { throw 'Node.js 24 is required. No global toolchain is changed by this script.' }
    $pnpmVersion = [string](& pnpm.cmd --version)
    if ($LASTEXITCODE -ne 0 -or $pnpmVersion -ne '11.20.0') { throw 'pnpm 11.20.0 is required.' }
    $ci = $env:CI
    Push-Location (Join-Path $script:RepositoryRoot 'dashboard-src')
    try {
        $env:CI = 'true'
        Invoke-Checked 'Frontend dependencies' { pnpm.cmd install --frozen-lockfile --store-dir (Join-Path $script:RepositoryRoot '.tmp\pnpm-store') }
    }
    finally { $env:CI = $ci; Pop-Location }
}

function Test-DesktopSources {
    & (Join-Path $script:RepositoryRoot 'tests\scripts\DesktopSources.Tests.ps1')
}

function Test-FrontendTypes {
    Push-Location (Join-Path $script:RepositoryRoot 'dashboard-src')
    try { Invoke-Checked 'Frontend types' { node.exe node_modules/vue-tsc/bin/vue-tsc.js --build --force } }
    finally { Pop-Location }
}

function Build-Frontend {
    $font = $env:FONT; $desktop = $env:DESKTOP_BUILD
    Push-Location (Join-Path $script:RepositoryRoot 'dashboard-src')
    try {
        $env:FONT = 'misans'; $env:DESKTOP_BUILD = '1'
        Invoke-Checked 'Desktop UI build' { node.exe node_modules/vite/bin/vite.js build }
    }
    finally { $env:FONT = $font; $env:DESKTOP_BUILD = $desktop; Pop-Location }
    $target = Join-Path $script:RepositoryRoot 'resources\dashboard'
    if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    New-Item -ItemType Directory -Force $target | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $script:RepositoryRoot 'dashboard-src\dist') -Force |
        Copy-Item -Destination $target -Recurse -Force
    & (Join-Path $script:PipelineDirectory 'assert-dashboard-assets.ps1') -Path $target
    & (Join-Path $script:PipelineDirectory 'create-app-icon.ps1')
    Test-StartupBundle -Path $target
}

function Test-StartupBundle {
    param([string]$Path)
    $manifest = [IO.File]::ReadAllText((Join-Path $Path '.vite\manifest.json')) | ConvertFrom-Json
    $roots = @($manifest.PSObject.Properties | Where-Object { $_.Value.isEntry -or $_.Name -eq 'src/appEntry.ts' } | ForEach-Object { $_.Name })
    if ('src/appEntry.ts' -notin $roots) { throw 'Application startup chunk was not found in the generated manifest.' }
    $seen = [Collections.Generic.HashSet[string]]::new()
    $pending = [Collections.Generic.Stack[string]]::new()
    foreach ($root in $roots) { $pending.Push($root) }
    while ($pending.Count) {
        $key = $pending.Pop()
        if (-not $seen.Add($key)) { continue }
        $entry = $manifest.PSObject.Properties[$key].Value
        if ($entry.file -match 'charts-vendor|MiniSparkline|TimeSeriesChart') { throw "Heavy chart code is eagerly loaded by startup: $($entry.file)" }
        foreach ($dependency in $entry.imports) { $pending.Push($dependency) }
    }
    Write-Host 'Startup bundle contains no eager chart dependency.'
}

function Test-Host {
    param([string]$Configuration, [bool]$IncludeWebViewIntegration, [bool]$IncludeRealCoreIntegration, [bool]$IncludePerformanceIntegration, [bool]$IncludeSlowIntegration)
    Push-Location $script:RepositoryRoot
    try {
        Invoke-Checked '.NET restore' { dotnet restore tests/Dashboard.Tests/Dashboard.Tests.csproj --nologo }
        $testArgs = @('test', 'tests/Dashboard.Tests/Dashboard.Tests.csproj', '-c', $Configuration, '--no-restore', '--nologo')
        $filters = @()
        if (-not $IncludeWebViewIntegration) { $filters += 'Category!=WebViewIntegration' }
        if (-not $IncludeSlowIntegration) { $filters += 'Category!=SlowIntegration' }
        if (-not $IncludeRealCoreIntegration) { $filters += 'Category!=RealCoreIntegration' }
        # Performance gets a separate testhost so unrelated tests do not pollute its heap baseline.
        $filters += 'Category!=PerformanceIntegration'
        $filters += 'Category!=OnlineUpgradeIntegration'
        if (-not ($IncludeWebViewIntegration -and $IncludeRealCoreIntegration)) { $filters += 'Category!=WebViewCoreIntegration' }
        if ($filters.Count) { $testArgs += @('--filter', ($filters -join '&')) }
        if (($IncludeRealCoreIntegration -or $IncludePerformanceIntegration) -and -not $env:DASHBOARD_TEST_CORES_DIR) { throw 'Prepare validation cores and set DASHBOARD_TEST_CORES_DIR before selecting real-core tests.' }
        Invoke-Checked '.NET tests and bridge fixtures' { & dotnet @testArgs }
        if ($IncludePerformanceIntegration) {
            Invoke-Checked 'Isolated desktop performance workload' {
                dotnet test tests/Dashboard.Tests/Dashboard.Tests.csproj -c $Configuration --no-restore --nologo --filter 'Category=PerformanceIntegration'
            }
        }
    }
    finally { Pop-Location }
}

function Test-Frontend {
    if (-not (Test-Path (Join-Path $script:RepositoryRoot '.tmp\bridge-fixtures-v2\bootstrap.json'))) { throw 'Production bridge fixture is missing; run host tests first.' }
    Push-Location (Join-Path $script:RepositoryRoot 'dashboard-src')
    $previousNodeEnv = $env:NODE_ENV
    try {
        $env:NODE_ENV = 'test'
        Invoke-Checked 'Frontend behavior and wire tests' { node.exe node_modules/vitest/vitest.mjs run }
    }
    finally { $env:NODE_ENV = $previousNodeEnv; Pop-Location }
}

function Invoke-Verification {
    param([string]$Configuration = 'Release', [bool]$IncludeWebViewIntegration = $false, [bool]$IncludeRealCoreIntegration = $false, [bool]$IncludePerformanceIntegration = $false, [bool]$IncludeSlowIntegration = $false)
    if ($IncludeSlowIntegration -and -not $IncludeWebViewIntegration) { throw 'IncludeSlowIntegration requires IncludeWebViewIntegration.' }
    Test-DesktopSources
    Prepare-Frontend
    Test-FrontendTypes
    Build-Frontend
    Test-Host -Configuration $Configuration -IncludeWebViewIntegration $IncludeWebViewIntegration -IncludeRealCoreIntegration $IncludeRealCoreIntegration -IncludePerformanceIntegration $IncludePerformanceIntegration -IncludeSlowIntegration $IncludeSlowIntegration
    Test-Frontend
    Write-Host "Automated verification passed ($Configuration). WebView: $IncludeWebViewIntegration; isolated real-core tests: $IncludeRealCoreIntegration; performance workload: $IncludePerformanceIntegration; slow lifecycle: $IncludeSlowIntegration. Manual system acceptance is separate." -ForegroundColor Green
}

function Publish-Dashboard {
    param([string]$Configuration = 'Release')
    $output = Join-Path $script:RepositoryRoot "artifacts\publish\Dashboard-$Configuration-win-x64"
    if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    Push-Location $script:RepositoryRoot
    try { Invoke-Checked '.NET publish' { dotnet publish Dashboard.csproj -c $Configuration -r win-x64 --no-restore --nologo -o $output --self-contained false /p:DebugType=None /p:DebugSymbols=false } }
    finally { Pop-Location }
    if (-not (Test-Path (Join-Path $output 'Dashboard.exe'))) { throw 'Published Dashboard.exe is missing.' }
    if ((Test-Path (Join-Path $output 'WebView2Loader.dll')) -and (Test-Path (Join-Path $output 'runtimes'))) { Remove-Item (Join-Path $output 'runtimes') -Recurse -Force }
    return $output
}

function Assert-PublishDirectory {
    param([string]$Path)
    if (-not (Test-Path (Join-Path $Path 'Dashboard.exe'))) { throw 'Published Dashboard.exe is missing.' }
    & (Join-Path $script:PipelineDirectory 'assert-dashboard-assets.ps1') -Path (Join-Path $Path 'resources\dashboard')
    $root = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $forbidden = Get-ChildItem $root -Recurse -File -Force | Where-Object {
        $relative = $_.FullName.Substring($root.Length + 1)
        $_.Name -eq 'settings.json' -or
        $_.Name -match '^(?:mihomo|sing-box)(?:[-.].*)?\.exe$' -or
        $_.Name -match '^config(?:[-.].*)?\.json$' -or
        $_.Extension -in '.log', '.pdb', '.yaml', '.yml' -or
        $relative -match '(?:^|[\\/])(?:webview-data(?:-v\d+)?|EBWebView|icon-cache|backups|profiles?|cores?|configs?)[\\/]'
    }
    if ($forbidden) { throw "Runtime/user data leaked into publish output: $($forbidden[0].Name)" }
}

function Assert-ReleaseArchive {
    param([string]$ArchivePath, [string]$PublishDirectory)
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\', '/')
    $expected = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($file in Get-ChildItem $root -Recurse -File -Force) {
        $expected.Add($file.FullName.Substring($root.Length + 1).Replace('\', '/'), $file.FullName)
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    $hash = [Security.Cryptography.SHA256]::Create()
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    try {
        if ($archive.Entries.Count -ne $expected.Count) { throw 'ZIP entry count does not match the publish directory.' }
        foreach ($entry in $archive.Entries) {
            if (-not $seen.Add($entry.FullName) -or -not $expected.ContainsKey($entry.FullName)) {
                throw "Unexpected or duplicate ZIP entry: $($entry.FullName)"
            }
            $stream = $entry.Open()
            try { $actual = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose() }
            if ($actual -ne (Get-FileHash -LiteralPath $expected[$entry.FullName] -Algorithm SHA256).Hash) {
                throw "ZIP content does not match the publish file: $($entry.FullName)"
            }
        }
    }
    finally { $hash.Dispose(); $archive.Dispose() }
    Write-Host "Verified ZIP names and SHA256 content for $($expected.Count) files."
}

function Write-ReleaseArchive {
    param([string]$PublishDirectory, [string]$OutputZip)
    if ([IO.Path]::GetFileName($OutputZip) -ne $OutputZip -or $OutputZip -notmatch '\.zip$') { throw 'OutputZip must be a ZIP filename, not a path.' }
    Assert-PublishDirectory -Path $PublishDirectory
    $directory = Join-Path $script:RepositoryRoot 'artifacts\releases'
    New-Item -ItemType Directory -Force $directory | Out-Null
    $output = Join-Path $directory $OutputZip
    $temporary = $output + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    try {
        $root = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\', '/')
        $files = @(Get-ChildItem $root -Recurse -File -Force)
        $writer = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($writer, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $writer.Dispose() }
        Assert-ReleaseArchive -ArchivePath $temporary -PublishDirectory $PublishDirectory
        if (Test-Path $output) { [IO.File]::Replace($temporary, $output, [NullString]::Value) }
        else { [IO.File]::Move($temporary, $output) }
    }
    finally { if (Test-Path $temporary) { Remove-Item -LiteralPath $temporary } }
    return $output
}
