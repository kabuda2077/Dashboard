#requires -Version 7.0
param([string]$SourceRoot = (Join-Path $PSScriptRoot '..\..\dashboard-src'))
$ErrorActionPreference = 'Stop'
$package = [IO.File]::ReadAllText((Join-Path $SourceRoot 'package.json')) | ConvertFrom-Json
# Product scope / packaging boundaries, not implementation names or CSS fragments.
$dependencies = @($package.dependencies.PSObject.Properties.Name) + @($package.devDependencies.PSObject.Properties.Name)
foreach ($name in @('@bufbuild/protobuf', '@connectrpc/connect', '@xterm/xterm', 'three')) {
    if ($name -in $dependencies) { throw "Unapproved desktop dependency: $name. A product decision is required." }
}
foreach ($file in @('index.html', 'src/main.ts', 'src/appEntry.ts', 'src/composables/hostBridge.ts')) {
    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot $file) -PathType Leaf)) { throw "Missing desktop entry: $file" }
}
Write-Host 'Desktop packaging source boundaries passed. Runtime behavior is covered by the unit/integration suites.'
