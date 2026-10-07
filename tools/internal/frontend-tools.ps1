#requires -Version 7.0
# Select project tool versions without changing global configuration or pinning
# any machine-specific cache/store path. pnpm owns runtime downloads and storage.
function Initialize-FrontendTools {
    $frontend = Join-Path $script:RepositoryRoot 'dashboard-src'
    $project = [IO.File]::ReadAllText((Join-Path $frontend 'package.json')) | ConvertFrom-Json
    if ($project.packageManager -notmatch '^pnpm@([^+]+)') { throw 'Declare the pnpm version in dashboard-src/package.json.' }
    $expectedPnpm = $Matches[1]
    $expectedNode = [IO.File]::ReadAllText((Join-Path $script:RepositoryRoot '.node-version')).Trim()
    if ($expectedNode -notmatch '^\d+\.\d+\.\d+$') { throw '.node-version must contain an exact Node version.' }
    $script:FrontendPnpm = (Get-Command pnpm.cmd -ErrorAction Stop).Source
    Push-Location $frontend
    try {
        # Check from the package directory so the pnpm launcher can honor packageManager.
        $actualPnpm = [string](& $script:FrontendPnpm --version)
        if ($LASTEXITCODE -ne 0 -or $actualPnpm.Trim() -ne $expectedPnpm) {
            throw "pnpm $expectedPnpm is required by packageManager; the frontend directory resolved $actualPnpm."
        }
        $script:FrontendNode = (Get-Command node.exe -ErrorAction SilentlyContinue).Source
        $actualNode = if ($script:FrontendNode) { [string](& $script:FrontendNode --version) } else { '' }
        if ($actualNode.Trim() -ne "v$expectedNode") {
            # A cached pnpm-managed runtime, not another project-local toolchain.
            $resolved = @(& $script:FrontendPnpm --reporter silent dlx "node@runtime:$expectedNode" --print process.execPath)
            if ($LASTEXITCODE -ne 0) { throw "Cannot resolve Node $expectedNode through pnpm." }
            $paths = @($resolved | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) })
            if ($paths.Count -ne 1) { throw 'pnpm did not return a unique Node executable path.' }
            $script:FrontendNode = [IO.Path]::GetFullPath($paths[0])
        }
        $actualNode = [string](& $script:FrontendNode --version)
        if ($LASTEXITCODE -ne 0 -or $actualNode.Trim() -ne "v$expectedNode") { throw 'Resolved Node version does not match .node-version.' }
        $store = [string](& $script:FrontendPnpm --reporter silent store path)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the configured pnpm store.' }
        Write-Host "pnpm launcher: $script:FrontendPnpm ($expectedPnpm)"
        Write-Host "Node: $script:FrontendNode ($expectedNode)"
        Write-Host "pnpm store: $($store.Trim())"
    }
    finally { Pop-Location }
}

function Invoke-FrontendNode {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    if (-not $script:FrontendNode) { Initialize-FrontendTools }
    $previousPath = $env:PATH
    try {
        $env:PATH = (Split-Path $script:FrontendNode -Parent) + [IO.Path]::PathSeparator + $previousPath
        & $script:FrontendNode @Arguments
    }
    finally { $env:PATH = $previousPath }
}
