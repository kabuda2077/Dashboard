#requires -Version 7.0
param(
    [string]$UpstreamRoot,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $UpstreamRoot) { $UpstreamRoot = Join-Path $root '.tmp\experiments\upstream-audit-v3.26.0' }
$UpstreamRoot = [IO.Path]::GetFullPath($UpstreamRoot)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root '.tmp\reports\upstream-current' }
$revision = [string](git -C $UpstreamRoot rev-parse HEAD)
if ($LASTEXITCODE -or $revision -ne 'b31d05f42702b121e0b17eab1e3697d5f1d6db8d') { throw 'The audit requires the exact reviewed upstream SHA.' }
if (@(git -C $UpstreamRoot status --porcelain).Count) { throw 'The upstream worktree must be clean.' }
$upstream = @(git -C $UpstreamRoot -c core.quotepath=false ls-files)
$local = @(git -C $root -c core.quotepath=false ls-files --cached --others --exclude-standard -- dashboard-src | ForEach-Object { $_.Substring('dashboard-src/'.Length) } | Where-Object { Test-Path -LiteralPath (Join-Path $root ('dashboard-src/'+$_)) -PathType Leaf })
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'source-fingerprint.ps1')
$rows = foreach ($file in @($upstream + $local | Sort-Object -Unique)) {
    $old = Join-Path $UpstreamRoot $file
    $new = Join-Path $root ('dashboard-src/'+$file)
    $oldHash = Get-NormalizedSourceHash $old; $newHash = Get-NormalizedSourceHash $new
    if ($oldHash -eq $newHash) { continue }
    $change = if ($oldHash -eq 'MISSING') { 'added' } elseif ($newHash -eq 'MISSING') { 'removed' } else { 'modified' }
    [pscustomobject]@{ path=$file; change=$change; upstreamSha256=$oldHash; localSha256=$newHash }
}
$rows | ConvertTo-Json -Depth 4 | Out-File (Join-Path $OutputDirectory 'inventory.json') -Encoding utf8
[ordered]@{ upstreamRevision = $revision; local = (Get-FrontendSnapshot $root); normalization = 'UTF-8 text with LF line endings; binary bytes unchanged; snapshot paths sorted ordinally'; differences = $rows.Count } |
    ConvertTo-Json -Depth 4 | Out-File (Join-Path $OutputDirectory 'audit.json') -Encoding utf8
$diff = [Collections.Generic.List[string]]::new()
foreach ($row in $rows | Where-Object { $_.change -eq 'modified' -and $_.path -match '\.(tsx?|vue|css|html|js|json)$' -and $_.path -ne 'pnpm-lock.yaml' }) {
    $diff.Add("`n### $($row.path)")
    $lines = @(git -c core.autocrlf=false -c core.safecrlf=false diff --no-index --no-color --ignore-space-at-eol --unified=2 -- (Join-Path $UpstreamRoot $row.path) (Join-Path $root ('dashboard-src/'+$row.path)))
    if ($LASTEXITCODE -gt 1) { throw "Diff failed for $($row.path)" }
    foreach ($line in $lines) { $diff.Add($line) }
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'modified.diff'), $diff, $utf8)
$rows | Group-Object change | Select-Object Name,Count | Format-Table -AutoSize
Write-Host "Inventory and full modified-source diff: $OutputDirectory"
exit 0
