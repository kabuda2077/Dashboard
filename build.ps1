param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$NuGetSource = 'https://api.nuget.org/v3/index.json'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

powershell -ExecutionPolicy Bypass -File .\tools\build-zashboard.ps1

$publishDir = Join-Path $repoRoot "bin\$Configuration\net9.0-windows\$Runtime\publish"
if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet restore -s $NuGetSource
dotnet publish -c $Configuration -r $Runtime --self-contained false /p:DebugType=None /p:DebugSymbols=false

Write-Host "Publish completed: $publishDir"
