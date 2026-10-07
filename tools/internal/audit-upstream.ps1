#requires -Version 7.0
param(
    [string]$UpstreamRoot,
    [string]$OutputDirectory = '',
    [string]$TargetRef = '',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..\..')
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$root = [IO.Path]::GetFullPath($RepositoryRoot)
. (Join-Path $PSScriptRoot 'fork-policy.ps1')
$policy = Read-ForkPolicy -Root $root
if (-not $UpstreamRoot) { $UpstreamRoot = Join-Path $root ('.tmp/experiments/upstream-audit-' + $policy.upstream.tag) }
$UpstreamRoot = [IO.Path]::GetFullPath($UpstreamRoot)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root '.tmp/reports/upstream-current' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

function Invoke-AuditGit {
    param([string]$Directory, [string[]]$Arguments)
    $lines = @(git -C $Directory -c core.quotepath=false @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed in $Directory" }
    return $lines
}
$revision = [string](Invoke-AuditGit $UpstreamRoot @('rev-parse', 'HEAD'))
if ($revision -cne $policy.upstream.revision) { throw 'The upstream checkout must be at the exact reviewed policy SHA.' }
if (@(Invoke-AuditGit $UpstreamRoot @('status', '--porcelain')).Count) { throw 'The upstream worktree must be clean.' }
$reference = [string](Invoke-AuditGit $root @('rev-parse', ($policy.reference.tag + '^{commit}')))
if ($reference -cne $policy.reference.revision) { throw 'The stable reference tag does not match its reviewed SHA.' }
# Prove mirror hashes came from real upstream blobs, not a locally blessed edit.
foreach ($mirror in $policy.mirrors) {
    $file = $mirror.path.Substring('dashboard-src/'.Length)
    if ((Get-NormalizedSourceHash (Join-Path $UpstreamRoot $file)) -cne $mirror.sha256) {
        throw "Mirror baseline is not the reviewed upstream source: $file"
    }
}
Test-ForkMirrors -Root $root -Policy $policy
$head = [string](Invoke-AuditGit $root @('rev-parse', 'HEAD'))
$target = if ($TargetRef) { [string](Invoke-AuditGit $UpstreamRoot @('rev-parse', ($TargetRef + '^{commit}'))) } else { $revision }
if ($TargetRef) { $null = Invoke-AuditGit $UpstreamRoot @('merge-base', '--is-ancestor', $revision, $target) }

# Read R from an isolated archive: never switch, stash or alter the candidate.
$scratch = Join-Path $root ('.tmp/experiments/upstream-reference-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$archive = Join-Path $scratch 'reference.zip'
$referenceRoot = Join-Path $scratch 'reference'
try {
    $null = Invoke-AuditGit $root @('archive', '--format=zip', "--output=$archive", $reference)
    Expand-Archive -LiteralPath $archive -DestinationPath $referenceRoot
    $upstreamFiles = @(Invoke-AuditGit $UpstreamRoot @('ls-files') | ForEach-Object { 'dashboard-src/' + $_ })
    $referenceFiles = @(Invoke-AuditGit $root @('ls-tree', '-r', '--name-only', $reference, '--', 'dashboard-src', 'src', 'Dashboard.csproj'))
    $candidateFiles = @(Invoke-AuditGit $root @('ls-files', '--cached', '--others', '--exclude-standard', '--', 'dashboard-src', 'src', 'Dashboard.csproj'))
    $rows = @(foreach ($file in @($upstreamFiles + $referenceFiles + $candidateFiles | Sort-Object -Unique)) {
        $u = if ($file.StartsWith('dashboard-src/')) { Get-NormalizedSourceHash (Join-Path $UpstreamRoot $file.Substring(14)) } else { 'MISSING' }
        $r = Get-NormalizedSourceHash (Join-Path $referenceRoot $file)
        $d = Get-NormalizedSourceHash (Join-Path $root $file)
        if ($u -ceq $r -and $r -ceq $d) { continue }
        [pscustomobject]@{
            path = $file; upstreamSha256 = $u; referenceSha256 = $r; candidateSha256 = $d
            upstreamToReference = Get-ForkRelation $u $r
            referenceToCandidate = Get-ForkRelation $r $d
            upstreamToCandidate = Get-ForkRelation $u $d
            changes = @(Get-ForkChangeIds $file $policy)
        }
    })
    $byPath = @{}
    foreach ($row in $rows) { $byPath[$row.path] = $row }
    $incoming = @(foreach ($file in @(Invoke-AuditGit $UpstreamRoot @('diff', '--name-only', '--no-renames', $revision, $target))) {
        $path = 'dashboard-src/' + $file
        $groups = @(Get-ForkChangeIds $path $policy -IncludeWatch)
        $overlap = $byPath.ContainsKey($path) -and $byPath[$path].upstreamToCandidate -ne 'same'
        [pscustomobject]@{
            path = $path; localOverlap = $overlap; changes = $groups
            attention = if ($overlap) { 'local-overlap' } elseif ($groups.Count) { 'watched-consumer-or-boundary' } else { 'unclassified' }
            checks = @($policy.changes | Where-Object { $_.id -in $groups } | ForEach-Object { $_.checks } | Sort-Object -Unique)
        }
    })
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    $utf8 = [Text.UTF8Encoding]::new($false)
    ConvertTo-Json -InputObject $rows -Depth 8 | Set-Content (Join-Path $OutputDirectory 'inventory.json') -Encoding utf8
    $audit = [ordered]@{
        upstreamRevision = $revision; referenceRevision = $reference; candidateHead = $head; targetRevision = $target
        candidateDirty = (@(Invoke-AuditGit $root @('status', '--porcelain')).Count -gt 0)
        frontendSnapshot = Get-FrontendSnapshot $root
        policySha256 = Get-NormalizedSourceHash (Join-Path $root 'docs/upstream/local-changes.json')
        normalization = 'CRLF normalized to LF for UTF-8 text; binary bytes unchanged. Formatting/comment differences are NOT automatically deemed safe.'
        differences = $rows.Count; unclassified = @($rows | Where-Object { -not $_.changes.Count } | ForEach-Object { $_.path })
        incoming = $incoming; pendingDecisions = @($policy.pending); knownGaps = @($policy.knownGaps)
        scope = 'Source comparison and explicit dependency triggers, not semantic approval, complete dependency analysis, test execution or release readiness.'
    }
    $audit | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $OutputDirectory 'audit.json') -Encoding utf8
    $report = [Collections.Generic.List[string]]::new()
    $report.Add("# Upstream / stable / candidate comparison`n`nU: $revision`n`nR: $reference`n`nD: $head (dirty: $($audit.candidateDirty))`n`nProposed U1: $target`n")
    $report.Add('| Change | Origin | Decision | Files (groups may overlap) | Checks to review, not executed |')
    $report.Add('| --- | --- | --- | ---: | --- |')
    foreach ($change in $policy.changes) {
        $count = @($rows | Where-Object { $change.id -in $_.changes }).Count
        $report.Add("| $($change.id) | $($change.origin) | $($change.decision) | $count | $($change.checks -join ', ') |")
    }
    $report.Add("`n## Unclassified — review, do not silently adopt")
    foreach ($file in $audit.unclassified) { $report.Add("- $file") }
    $report.Add("`n## Incoming changes — no automatic merge approval")
    foreach ($item in $incoming) { $report.Add("- $($item.path): $($item.attention); review $($item.changes -join ', ')") }
    $report.Add("`n## Pending product/visual decisions")
    foreach ($item in $policy.pending) { $report.Add("- $($item.id): $($item.question)") }
    $report.Add("`n## Known gaps — a green tooling check does not resolve these")
    foreach ($item in $policy.knownGaps) { $report.Add("- $($item.id): $($item.observed); $($item.coverage)") }
    $report.Add("`n$($audit.scope)")
    [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'report.md'), $report, $utf8)
    $diff = [Collections.Generic.List[string]]::new()
    foreach ($row in $rows | Where-Object { $_.path.StartsWith('dashboard-src/') -and $_.upstreamToCandidate -eq 'modified' -and $_.path -match '\.(tsx?|vue|css|html|js|json)$' -and $_.path -notmatch 'pnpm-lock' }) {
        $diff.Add("`n### $($row.path)")
        $lines = @(git -c core.autocrlf=false -c core.safecrlf=false diff --no-index --no-color --ignore-space-at-eol --unified=2 -- (Join-Path $UpstreamRoot $row.path.Substring(14)) (Join-Path $root $row.path))
        if ($LASTEXITCODE -gt 1) { throw "Diff failed: $($row.path)" }
        foreach ($line in $lines) { $diff.Add($line) }
    }
    [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'modified.diff'), $diff, $utf8)
    $stableDiff = @(Invoke-AuditGit $root @('diff', '--no-ext-diff', '--no-color', $reference, '--', 'dashboard-src', 'src', 'Dashboard.csproj'))
    [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'reference-to-candidate.diff'), $stableDiff, $utf8)
    Write-Host "Compared $($rows.Count) changed paths; $($audit.unclassified.Count) unclassified; $($incoming.Count) incoming. Evidence: $OutputDirectory"
    Write-Host "Known gaps: $(@($policy.knownGaps).Count); pending decisions: $(@($policy.pending).Count). This is NOT a visual/release pass."
}
finally {
    # The archive has no working-copy links; only remove the directory owned by this invocation.
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
exit 0
