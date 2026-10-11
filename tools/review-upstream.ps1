#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$UpstreamRoot,
    [string]$TargetRef = '',
    [string]$OutputDirectory = '',
    [string]$DecisionFile = '',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [ValidateSet('Plan', 'Review', 'Result', 'Close')][string]$Phase = 'Plan',
    [switch]$ResetResults
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$root = [IO.Path]::GetFullPath($RepositoryRoot)
. (Join-Path $PSScriptRoot 'internal/fork-policy.ps1')

function Invoke-AuditGit {
    param([string]$Directory, [string[]]$Arguments)
    $lines = @(git --no-replace-objects -C $Directory -c core.quotepath=false @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed in $Directory" }
    return $lines
}
function Get-AuditPaths {
    param([string]$Directory, [string[]]$Arguments)
    $result = @(Invoke-AuditGit $Directory ($Arguments + @('-z')))
    if ($result.Count) { ($result -join "`n").Split([char]0, [StringSplitOptions]::RemoveEmptyEntries) }
}
function Get-AuditDigest {
    param([string]$Text)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}
function Get-AuditSourceSnapshot {
    $head = [string](Invoke-AuditGit $root @('rev-parse', 'HEAD'))
    $files = [string[]]@(Get-AuditPaths $root @('ls-files', '--cached', '--others', '--exclude-standard') | Sort-Object -Unique)
    [Array]::Sort($files, [StringComparer]::Ordinal)
    $manifest = @(foreach ($file in $files) {
        $path = Join-Path $root $file
        $hash = if (Test-Path -LiteralPath $path -PathType Leaf) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'MISSING' }
        [ordered]@{ path = $file; sha256 = $hash }
    })
    $fingerprint = @($manifest | ForEach-Object { "$($_.path)`t$($_.sha256)" })
    return [pscustomobject]@{ head = $head; files = $files; manifest = $manifest; hash = Get-AuditDigest ($head + "`n" + ($fingerprint -join "`n")) }
}
function Assert-AuditOutputPath {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.Equals($UpstreamRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($UpstreamRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Reports and decision files must be outside the read-only upstream checkout.'
    }
    for ($ancestor = $full; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        try { $attributes = [IO.File]::GetAttributes($ancestor) }
        catch [IO.FileNotFoundException] { continue }
        catch [IO.DirectoryNotFoundException] { continue }
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reports and decision files must not use reparse points or symlink ancestors: $ancestor" }
    }
    $relative = [IO.Path]::GetRelativePath($root, $full).Replace('\', '/')
    if ($relative -eq '.' -or ($relative -notmatch '^\.\./' -and -not [IO.Path]::IsPathRooted($relative))) {
        $null = git --no-replace-objects -C $root check-ignore --no-index --quiet -- $relative
        if ($LASTEXITCODE -ne 0) { throw 'Reports and decision files inside the candidate must be in ignored output paths, such as .tmp/reports; source files are read-only.' }
    }
    foreach ($file in $startSnapshot.files) {
        if ($full.Equals([IO.Path]::GetFullPath((Join-Path $root $file)), [StringComparison]::OrdinalIgnoreCase)) { throw 'Output includes git-visible source inputs; choose a separate ignored report directory and decision file.' }
    }
}
function Write-AuditOutput {
    param([string]$Path, [string]$Content)
    Assert-AuditOutputPath $Path
    $temporary = Join-Path ([IO.Path]::GetDirectoryName($Path)) ('.audit-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $Content | Set-Content -LiteralPath $temporary -Encoding utf8
        Assert-AuditOutputPath $Path
        # Replace the directory entry instead of truncating an existing hard-linked source file.
        [IO.File]::Move($temporary, $Path, $true)
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
function Write-ReviewJson {
    param([string]$Name, $Value)
    Write-AuditOutput (Join-Path $OutputDirectory $Name) (ConvertTo-Json -InputObject $Value -Depth 40)
}
function Read-ReviewJson {
    param([string]$Path)
    Assert-AuditOutputPath $Path
    return [IO.File]::ReadAllText($Path) | ConvertFrom-Json
}
function Assert-AuditGitMetadata {
    param([string]$Directory)
    $grafts = [string](Invoke-AuditGit $Directory @('rev-parse', '--git-path', 'info/grafts'))
    if (-not [IO.Path]::IsPathRooted($grafts)) { $grafts = Join-Path $Directory $grafts }
    if (Test-Path -LiteralPath $grafts) { throw 'Git graft metadata is unsupported; remove fixture grafts before planning.' }
}
function Assert-StableSources {
    Assert-AuditGitMetadata $root
    Assert-AuditGitMetadata $UpstreamRoot
    if ((Get-AuditSourceSnapshot).hash -cne $startSnapshot.hash) { throw 'Candidate source inputs changed during review; rerun with stable inputs.' }
    if ([string](Invoke-AuditGit $UpstreamRoot @('rev-parse', 'HEAD')) -cne $checkoutRevision -or
        @(Invoke-AuditGit $UpstreamRoot @('status', '--porcelain', '--untracked-files=all')).Count) { throw 'Reviewed upstream source changed; restore a clean exact checkout.' }
}
function Get-PlanHash {
    param($Value, [string]$HashField = 'planInputHash')
    $copy = $Value | ConvertTo-Json -Depth 40 | ConvertFrom-Json
    $copy.PSObject.Properties.Remove($HashField)
    return Get-AuditDigest ($copy | ConvertTo-Json -Depth 40 -Compress)
}
function Get-DecisionHash {
    param($Value)
    # Reasons and dispositions are the preimplementation decision; results never refresh this hash.
    $copy = $Value | ConvertTo-Json -Depth 40 | ConvertFrom-Json
    $copy.PSObject.Properties.Remove('resultInputHash')
    foreach ($batch in $copy.batches) { $batch.PSObject.Properties.Remove('result') }
    return Get-AuditDigest ($copy | ConvertTo-Json -Depth 40 -Compress)
}
function Test-ReviewBatches {
    param($Plan, $Review)
    if ($Review.schemaVersion -ne 3) { throw 'Unsupported decision file version.' }
    foreach ($field in @('upstreamRevision', 'targetRevision', 'planInputHash')) {
        if ($Review.$field -isnot [string] -or $Review.$field -cne $Plan.$field) { throw "Stale review $field; use the original immutable plan." }
    }
    if (@($Plan.unclassified).Count) { throw 'Unclassified local ownership remains; classify the candidate before creating a new plan.' }
    if (@($Plan.unclassifiedDivergence).Count) { throw 'Shared divergence without contracts remains; record the behavior before creating a new plan.' }
    Assert-ForkArray $Review.batches 'batches' -Strings:$false
    Assert-ForkArray $Review.overlapExceptions 'overlapExceptions'
    $required = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($path in $Plan.requiredPaths) { $null = $required.Add($path) }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $coverage = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($batch in $Review.batches) {
        Assert-ForkText $batch.id 'batch.id'
        if ($batch.id -cnotmatch '^[a-z][a-z0-9-]+$' -or -not $ids.Add($batch.id)) { throw "Invalid or duplicate batch ID: $($batch.id)" }
        Assert-ForkArray $batch.paths 'batch.paths' -NonEmpty -Strings
        if ($batch.action -cnotin @('adopt', 'adapt', 'replace-local', 'keep-local', 'defer')) { throw "Unresolved or invalid batch action: $($batch.id)" }
        Assert-ForkText $batch.reason 'batch.reason'
        Assert-ForkArray $batch.validation 'batch.validation' -NonEmpty -Strings
        Assert-ForkArray $batch.dependencies 'batch.dependencies' -Strings
        if ($batch.action -eq 'defer') { Assert-ForkText $batch.reReviewWhen 'defer.reReviewWhen' }
        if ($batch.action -eq 'replace-local') { Assert-ForkText $batch.upstreamEquivalent 'replace-local.upstreamEquivalent' }
        if ($null -ne $batch.localPaths) {
            Assert-ForkArray $batch.localPaths 'batch.localPaths' -Strings
            foreach ($path in $batch.localPaths) { Assert-ForkRelativePath $path }
        }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($path in $batch.paths) {
            if (-not $required.Contains($path) -or -not $seen.Add($path)) { throw "Unknown or duplicate batch path: $path" }
            if (-not $coverage.ContainsKey($path)) { $coverage[$path] = [Collections.Generic.List[string]]::new() }
            $coverage[$path].Add($batch.id)
        }
    }
    if ($coverage.Count -ne $required.Count) { throw 'Every incoming and carried deferred path requires batch coverage.' }
    $exceptions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $dispositions = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($exception in $Review.overlapExceptions) {
        Assert-ForkText $exception.reason 'overlap.reason'
        Assert-ForkArray $exception.batches 'overlap.batches' -NonEmpty -Strings
        if ($exception.path -isnot [string] -or -not $coverage.ContainsKey($exception.path) -or
            $coverage[$exception.path].Count -lt 2 -or -not $exceptions.Add($exception.path) -or
            (@($exception.batches | Sort-Object -Unique) -join "`n") -cne (@($coverage[$exception.path] | Sort-Object) -join "`n")) { throw "Invalid overlap exception: $($exception.path)" }
        if ($exception.action -cnotin @($Review.batches | Where-Object id -cin $exception.batches | ForEach-Object action)) { throw "Overlap must select a declared disposition: $($exception.path)" }
        $dispositions[$exception.path] = $exception.action
    }
    foreach ($path in $required) {
        $actions = @($Review.batches | Where-Object id -cin $coverage[$path] | ForEach-Object action | Sort-Object -Unique)
        $sameUnchangedAction = $actions.Count -eq 1 -and $actions[0] -in @('keep-local', 'defer')
        if ($coverage[$path].Count -gt 1 -and -not $sameUnchangedAction -and -not $exceptions.Contains($path)) { throw "Batch overlap requires an explicit exception: $path" }
        if (-not $dispositions.ContainsKey($path)) { $dispositions[$path] = ($Review.batches | Where-Object id -ceq $coverage[$path][0]).action }
    }
    foreach ($pending in $Plan.pendingDecisions) {
        if ($null -ne $pending.paths -and $pending.id -cnotin @($Review.batches.id)) { throw "Carried defer requires its original batch ID: $($pending.id)" }
        if ($null -ne $pending.paths) {
            $batch = $Review.batches | Where-Object id -ceq $pending.id
            foreach ($path in $pending.paths) { if ($path -cnotin $batch.paths) { throw "Carried defer lost path: $path" } }
        }
    }
    return ,$dispositions
}
function New-ReviewReport {
    param($Plan, $Review = $null, $Results = $null)
    $report = [Collections.Generic.List[string]]::new()
    $report.Add("# Upstream review`n`nU0: $($Plan.upstreamRevision)`nU1: $($Plan.targetRevision)`nD0: $($Plan.candidateSnapshot.head) (dirty: $($Plan.candidateDirty))`nPlan: $($Plan.planInputHash)`n")
    $report.Add("Incoming: $($Plan.incoming.Count); shared divergence: $($Plan.shared.Count); carried deferred: $(@($Plan.pendingDecisions | Where-Object { $null -ne $_.paths }).Count). Path coverage and Git hints are evidence, not semantic approval.")
    $report.Add("`n## Functional decisions and results")
    if ($null -eq $Review) { $report.Add('Group related changes in decisions.json. Unknown changes are pending; no disposition has been approved.') }
    else {
        foreach ($batch in $Review.batches) {
            $report.Add("`n### $($batch.id) — $($batch.action) ($($batch.paths.Count) upstream paths; $(@($batch.localPaths).Count) local paths)")
            $report.Add($batch.reason)
            $report.Add("Planned validation: $($batch.validation -join '; ')")
            if ($batch.action -eq 'defer') { $report.Add("Reconsider: $($batch.reReviewWhen); dependencies: $($batch.dependencies -join ', ')") }
            if ($null -ne $Results) {
                $report.Add("Recorded outcome: $($batch.result.outcome). Actual evidence: $($batch.result.validation -join '; ')")
                if ($batch.result.deviations) { $report.Add("Implementation deviations: $($batch.result.deviations)") }
                if ($batch.result.replacement) { $report.Add("Replacement: $($batch.result.replacement | ConvertTo-Json -Compress)") }
            }
        }
    }
    if ($null -ne $Results) { $report.Add("`nD0→D1: $($Results.changedPaths.Count) declared changed paths. Full raw-hash change manifest: results.json. Deferred batches: $($Results.deferred.Count). Baseline closure remains a separate step.") }
    $report.Add("`n## Affected rules`n`n| Contract | Risk | Rule |`n| --- | --- | --- |")
    foreach ($contract in $Plan.affectedContracts) { $report.Add("| $($contract.id) | $($contract.risk) | $($contract.rule) |") }
    $suggestedChecks = @($Plan.checks)
    $checkScope = 'Target affected checks; select by actual implementation'
    if ($null -ne $Review) {
        $implementedIds = @($Review.batches | Where-Object { $_.action -in @('adopt', 'adapt', 'replace-local') } | ForEach-Object {
            foreach ($path in @($_.paths) + @($_.localPaths)) { Get-ForkChangeIds $path $Plan.policy -IncludeWatch }
        } | Sort-Object -Unique)
        $suggestedChecks = @($Plan.policy.contracts | Where-Object id -cin $implementedIds | ForEach-Object checks | Sort-Object -Unique)
        $checkScope = 'Checks associated with implemented batches; full target impact remains in plan.json'
    }
    $report.Add("`n<details>`n<summary>$checkScope ($($suggestedChecks.Count), deduplicated; not executed)</summary>`n")
    foreach ($check in $suggestedChecks) { $report.Add("- $check") }
    $report.Add("`n</details>")
    $report.Add("`n## Path evidence`n`nFull incoming paths and coverage requirements: plan.json. Initial ownership and byte comparisons: inventory.json.")
    $report.Add("`n## Blockers")
    foreach ($path in @($Plan.unclassified) + @($Plan.unclassifiedDivergence)) { $report.Add("- $path") }
    $report.Add("`n<details>`n<summary>Git hints ($($Plan.renameHints.Count) renames, $($Plan.commitHints.Count) commits; content review required)</summary>`n")
    foreach ($hint in @($Plan.renameHints) + @($Plan.commitHints)) { $report.Add("- $hint") }
    $report.Add("`n</details>")
    $report.Add("`n## Persistent pending and known gaps")
    foreach ($item in $Plan.pendingDecisions) { $report.Add("- $($item.id): $($item.reason)$($item.question); reconsider $($item.reReviewWhen)") }
    foreach ($item in $Plan.knownGaps) { $report.Add("- $($item.id): $($item.observed); $($item.coverage)") }
    $report.Add("`nReview validates coverage and declared evidence. Result validates source binding and explicit file provenance. Neither executes tests nor certifies semantic equivalence, product approval, or release readiness.")
    return $report -join [Environment]::NewLine
}

Assert-AuditGitMetadata $root
$startSnapshot = Get-AuditSourceSnapshot
$currentPolicy = Read-ForkPolicy -Root $root
if (-not $UpstreamRoot) { $UpstreamRoot = Join-Path $root ('.tmp/experiments/upstream-audit-' + $currentPolicy.upstream.tag) }
$UpstreamRoot = [IO.Path]::GetFullPath($UpstreamRoot)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root '.tmp/reports/upstream-current' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($destination in @('plan.json', 'inventory.json', 'decisions.json', 'review.json', 'results.json', 'closure.json', 'report.md')) { Assert-AuditOutputPath (Join-Path $OutputDirectory $destination) }
if (-not $DecisionFile) { $DecisionFile = Join-Path $OutputDirectory 'decisions.json' }
$DecisionFile = [IO.Path]::GetFullPath($DecisionFile)
Assert-AuditOutputPath $DecisionFile
foreach ($generated in @('plan.json', 'inventory.json', 'review.json', 'results.json', 'closure.json', 'report.md')) {
    if ($DecisionFile.Equals((Join-Path $OutputDirectory $generated), [StringComparison]::OrdinalIgnoreCase)) { throw 'DecisionFile must be the editable decision record, not generated evidence.' }
}
Assert-AuditGitMetadata $UpstreamRoot
$checkoutRevision = [string](Invoke-AuditGit $UpstreamRoot @('rev-parse', 'HEAD'))
if (@(Invoke-AuditGit $UpstreamRoot @('status', '--porcelain', '--untracked-files=all')).Count) { throw 'The upstream worktree must be clean.' }
if ($ResetResults -and $Phase -ne 'Result') { throw 'ResetResults is only valid in Result phase.' }
foreach ($mapping in $currentPolicy.adaptations) {
    if ((Get-ForkUpstreamHash $UpstreamRoot $mapping.revision $mapping.upstreamPath.Substring($currentPolicy.upstream.localPrefix.Length)) -ceq 'MISSING') { throw "Adaptation upstream source does not exist: $($mapping.upstreamPath)" }
}
$planPath = Join-Path $OutputDirectory 'plan.json'
if ($Phase -eq 'Plan') {
    if ($checkoutRevision -cne $currentPolicy.upstream.revision) { throw 'The upstream checkout must be at the exact reviewed policy SHA.' }
    $target = if ($TargetRef) { [string](Invoke-AuditGit $UpstreamRoot @('rev-parse', ($TargetRef + '^{commit}'))) } else { $checkoutRevision }
    $null = Invoke-AuditGit $UpstreamRoot @('merge-base', '--is-ancestor', $checkoutRevision, $target)
    Test-ForkMirrors -Root $root -Policy $currentPolicy -UpstreamRoot $UpstreamRoot
    if (Test-Path -LiteralPath $planPath) {
        $saved = Read-ReviewJson $planPath
        if ($saved.planInputHash -cne (Get-PlanHash $saved) -or $saved.candidateSnapshot.hash -cne $startSnapshot.hash -or
            $saved.upstreamRevision -cne $checkoutRevision -or $saved.targetRevision -cne $target -or
            $saved.inventoryHash -cne (Get-AuditDigest ([IO.File]::ReadAllText((Join-Path $OutputDirectory 'inventory.json'))))) { throw 'Immutable plan already exists for different inputs; choose a new output directory.' }
        Assert-StableSources
        Write-Host "Existing immutable plan preserved: $planPath"
        return
    }
    foreach ($name in @('inventory.json', 'review.json', 'results.json', 'closure.json', 'decisions.json')) {
        if (Test-Path -LiteralPath (Join-Path $OutputDirectory $name)) { throw 'Existing review evidence without its plan; choose a new output directory.' }
    }
    if (Test-Path -LiteralPath $DecisionFile) { throw 'Existing editable decision record without its plan; choose a new decision file.' }
    $prefix = $currentPolicy.upstream.localPrefix
    $upstreamFiles = @(Get-AuditPaths $UpstreamRoot @('ls-tree', '-r', '--name-only', $checkoutRevision) | ForEach-Object { $prefix + $_ })
    $paths = [string[]]@($upstreamFiles + $startSnapshot.files | Sort-Object -Unique)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $rows = @(foreach ($file in $paths) {
        $inUpstream = $file -cin $upstreamFiles
        $owned = Test-ForkPathMatch $file @($currentPolicy.forkOwnedPaths)
        $retained = $file -cin @($currentPolicy.pending | ForEach-Object paths) -or $file -cin @($currentPolicy.mirrors.path) -or $file -cin @($currentPolicy.adaptations.path)
        if ($owned -and $inUpstream) { throw "Fork-owned path collides with reviewed upstream: $file" }
        $u = if ($inUpstream) { Get-NormalizedSourceHash (Join-Path $UpstreamRoot $file.Substring($prefix.Length)) } else { 'MISSING' }
        $d = Get-NormalizedSourceHash (Join-Path $root $file)
        $relation = Get-ForkRelation $u $d
        [ordered]@{ path = $file; upstreamSha256 = $u; candidateSha256 = $d; upstreamToCandidate = $relation
            ownership = if ($u -eq 'MISSING' -and $d -eq 'MISSING') { 'absent' } elseif ($owned) { 'fork-owned' } elseif (($inUpstream -and $relation -ne 'same') -or $retained) { 'shared' } elseif ($inUpstream) { 'upstream' } else { 'unclassified' }
            contracts = @(Get-ForkChangeIds $file $currentPolicy) }
    })
    $byPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($row in $rows) { $byPath[$row.path] = $row }
    $incoming = @(foreach ($file in @(Get-AuditPaths $UpstreamRoot @('diff', '--name-only', '--no-renames', $checkoutRevision, $target))) {
        $path = $prefix + $file
        $direct = @(Get-ForkChangeIds $path $currentPolicy)
        $groups = @(Get-ForkChangeIds $path $currentPolicy -IncludeWatch)
        $contracts = @($currentPolicy.contracts | Where-Object id -cin $groups)
        $owned = Test-ForkPathMatch $path @($currentPolicy.forkOwnedPaths)
        $overlap = $byPath.ContainsKey($path) -and $byPath[$path].upstreamToCandidate -ne 'same'
        $risk = 'low'
        foreach ($contract in $contracts) { if ($contract.risk -eq 'high' -or ($contract.risk -eq 'medium' -and $risk -eq 'low')) { $risk = $contract.risk } }
        if (-not $groups.Count) { $risk = 'high' }
        [ordered]@{ path = $path; localOverlap = $overlap; contracts = $groups; risk = $risk
            attention = if ($owned) { 'fork-owned-collision' } elseif ($overlap) { 'shared-overlap' } elseif ($direct.Count) { 'upstream-change' } elseif ($groups.Count) { 'contract-watch' } else { 'unclassified' }
            targetSha256 = Get-ForkUpstreamHash $UpstreamRoot $target $file }
    })
    $affectedIds = @($incoming | ForEach-Object contracts | Sort-Object -Unique)
    foreach ($pending in $currentPolicy.pending) {
        if ($pending.paths) { foreach ($path in $pending.paths) { $affectedIds += @(Get-ForkChangeIds $path $currentPolicy -IncludeWatch) } }
    }
    $affected = @($currentPolicy.contracts | Where-Object id -cin $affectedIds | ForEach-Object {
        [ordered]@{ id = $_.id; risk = $_.risk; rule = if ($_.rule) { $_.rule } else { "docs/maintenance.md#$($_.id)" } }
    })
    $inventoryText = (ConvertTo-Json -InputObject $rows -Depth 15) + [Environment]::NewLine
    $plan = [ordered]@{
        schemaVersion = 3; upstreamRevision = $checkoutRevision; targetRevision = $target; candidateSnapshot = $startSnapshot
        candidateDirty = (@(Invoke-AuditGit $root @('status', '--porcelain')).Count -gt 0); policy = $currentPolicy
        normalization = 'Inventory normalizes CRLF to LF for UTF-8; binary bytes unchanged. Source binding uses raw hashes. Comments or formatting imply no approval.'
        inventoryHash = Get-AuditDigest $inventoryText; incoming = $incoming
        shared = @($rows | Where-Object ownership -eq 'shared' | ForEach-Object path)
        unclassified = @($rows | Where-Object ownership -eq 'unclassified' | ForEach-Object path)
        unclassifiedDivergence = @($rows | Where-Object { $_.ownership -eq 'shared' -and -not $_.contracts.Count } | ForEach-Object path)
        requiredPaths = @(@($incoming.path) + @($currentPolicy.pending | ForEach-Object { $_.paths }) | Where-Object { $_ } | Sort-Object -Unique)
        affectedContracts = $affected; checks = @($currentPolicy.contracts | Where-Object id -cin $affectedIds | ForEach-Object checks | Sort-Object -Unique)
        pendingDecisions = @($currentPolicy.pending); knownGaps = @($currentPolicy.knownGaps)
        renameHints = @(Invoke-AuditGit $UpstreamRoot @('diff', '--name-status', '-M', $checkoutRevision, $target) | Where-Object { $_ -cmatch '^R[0-9]+\s' })
        commitHints = @(Invoke-AuditGit $UpstreamRoot @('log', '--format=%H %s', '--reverse', ($checkoutRevision + '..' + $target)))
    }
    $plan.planInputHash = Get-PlanHash $plan
    $review = [ordered]@{ schemaVersion = 3; upstreamRevision = $checkoutRevision; targetRevision = $target; planInputHash = $plan.planInputHash
        batches = @(); overlapExceptions = @() }
    Assert-StableSources
    $null = New-Item -ItemType Directory -Force $OutputDirectory
    $null = New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($DecisionFile))
    Write-AuditOutput (Join-Path $OutputDirectory 'inventory.json') $inventoryText.TrimEnd("`r", "`n")
    Write-ReviewJson 'plan.json' $plan
    Write-AuditOutput $DecisionFile ($review | ConvertTo-Json -Depth 40)
    Write-AuditOutput (Join-Path $OutputDirectory 'report.md') (New-ReviewReport $plan)
    Write-Host "Immutable plan created; $($incoming.Count) incoming paths; $($plan.requiredPaths.Count) paths require functional batches. Edit $DecisionFile"
    return
}
$plan = Read-ReviewJson $planPath
if ($plan.schemaVersion -ne 3 -or $plan.planInputHash -cne (Get-PlanHash $plan) -or
    $plan.inventoryHash -cne (Get-AuditDigest ([IO.File]::ReadAllText((Join-Path $OutputDirectory 'inventory.json'))))) { throw 'Immutable plan evidence was modified.' }
if ($TargetRef -and [string](Invoke-AuditGit $UpstreamRoot @('rev-parse', ($TargetRef + '^{commit}'))) -cne $plan.targetRevision) { throw 'TargetRef differs from immutable plan.' }
$expectedCheckout = if ($Phase -eq 'Close') { $plan.targetRevision } else { $plan.upstreamRevision }
if ($checkoutRevision -cne $expectedCheckout) { throw "Upstream checkout must be at $expectedCheckout for $Phase." }
$review = Read-ReviewJson $DecisionFile
$dispositions = Test-ReviewBatches $plan $review
$decisionHash = Get-DecisionHash $review
$reviewPath = Join-Path $OutputDirectory 'review.json'
if ($Phase -eq 'Review') {
    if ($startSnapshot.hash -cne $plan.candidateSnapshot.hash) { throw 'Candidate differs from preimplementation plan; use Result for implementation changes.' }
    Assert-StableSources
    if (Test-Path -LiteralPath $reviewPath) {
        $approved = Read-ReviewJson $reviewPath
        if ($approved.planInputHash -cne $plan.planInputHash -or $approved.decisionHash -cne $decisionHash) { throw 'Immutable preimplementation decisions changed; create a new plan.' }
    }
    else { Write-ReviewJson 'review.json' ([ordered]@{ schemaVersion = 3; planInputHash = $plan.planInputHash; decisionHash = $decisionHash; batches = $review.batches; overlapExceptions = $review.overlapExceptions }) }
    Write-AuditOutput (Join-Path $OutputDirectory 'report.md') (New-ReviewReport $plan $review)
    Write-Host 'Preimplementation batch decisions frozen. No tests or semantic approval performed.'
    return
}
$approved = Read-ReviewJson $reviewPath
if ($approved.planInputHash -cne $plan.planInputHash -or $approved.decisionHash -cne $decisionHash) { throw 'Decisions differ from frozen preimplementation review; old reasons cannot be rebound to a new implementation.' }
$resultsPath = Join-Path $OutputDirectory 'results.json'
if ($Phase -eq 'Result') {
    if ($currentPolicy.upstream.revision -cne $plan.upstreamRevision) { throw 'Do not advance the comparison baseline before completing Result.' }
    if (Test-Path -LiteralPath $resultsPath) {
        $saved = Read-ReviewJson $resultsPath
        if ($saved.resultHash -cne (Get-PlanHash $saved 'resultHash')) { throw 'Immutable result evidence was modified.' }
        if ($ResetResults) { throw 'Completed results are immutable; create a new review round for further implementation.' }
        if ($saved.candidateSnapshot.hash -cne $startSnapshot.hash -or $saved.decisionHash -cne $decisionHash -or
            $saved.recordHash -cne (Get-AuditDigest ($review | ConvertTo-Json -Depth 40 -Compress))) { throw 'Result source or decisions changed; previous validation cannot be reused.' }
        Assert-StableSources
        Write-Host 'Existing immutable results match the current source and decisions.'
        return
    }
    if ($ResetResults -or -not $review.resultInputHash) {
        $review | Add-Member -Force NoteProperty resultInputHash $startSnapshot.hash
        foreach ($batch in $review.batches) {
            $batch | Add-Member -Force NoteProperty result ([pscustomobject]@{ outcome = ''; deviations = ''; validation = @(); replacement = $null })
        }
        Assert-StableSources
        Write-AuditOutput $DecisionFile ($review | ConvertTo-Json -Depth 40)
        Write-Host 'Result source captured. Fill each batch.result with actual evidence, then rerun Result. No completion recorded.'
        return
    }
    if ($review.resultInputHash -cne $startSnapshot.hash) { throw 'Result source changed; use -ResetResults to discard old validation and capture the new source.' }
    $initialFiles = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $actualFiles = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($file in $plan.candidateSnapshot.manifest) { $initialFiles[$file.path] = $file.sha256 }
    foreach ($file in $startSnapshot.manifest) { $actualFiles[$file.path] = $file.sha256 }
    $changedPaths = @(foreach ($path in @(@($initialFiles.Keys) + @($actualFiles.Keys) | Sort-Object -Unique)) {
        $beforeHash = if ($initialFiles.ContainsKey($path)) { $initialFiles[$path] } else { 'MISSING' }
        $afterHash = if ($actualFiles.ContainsKey($path)) { $actualFiles[$path] } else { 'MISSING' }
        if ($beforeHash -cne $afterHash) {
            $covering = @($review.batches | Where-Object { $path -cin $_.paths -or $path -cin $_.localPaths } | ForEach-Object id)
            if (-not $covering.Count) { throw "Actual source change is outside declared batch paths/localPaths: $path" }
            [ordered]@{ path = $path; beforeSha256 = $beforeHash; afterSha256 = $afterHash; batches = $covering }
        }
    })
    foreach ($batch in $review.batches) {
        if ($batch.result.outcome -cne $batch.action) { throw "Result outcome must match the declared disposition (revise decisions explicitly if needed): $($batch.id)" }
        Assert-ForkArray $batch.result.validation 'result.validation' -NonEmpty -Strings
        if ($batch.result.deviations -isnot [string]) { throw 'result.deviations must be a string (empty means no deviation).' }
        if ($batch.action -eq 'replace-local') {
            foreach ($field in @('newImplementation', 'oldImplementationExit', 'retainedAdaptation')) { Assert-ForkText $batch.result.replacement.$field "replacement.$field" }
        }
    }
    $inventory = @(Read-ReviewJson (Join-Path $OutputDirectory 'inventory.json'))
    $before = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($row in $inventory) { $before[$row.path] = $row }
    foreach ($path in $plan.requiredPaths) {
        $action = $dispositions[$path]
        $hash = Get-NormalizedSourceHash (Join-Path $root $path)
        if ($action -eq 'adopt') {
            $item = @($plan.incoming | Where-Object path -ceq $path)
            $targetHash = if ($item.Count) { $item[0].targetSha256 } else { Get-ForkUpstreamHash $UpstreamRoot $plan.targetRevision $path.Substring($plan.policy.upstream.localPrefix.Length) }
            if ($hash -cne $targetHash) { throw "Adopt must match exact target source (including deletions): $path" }
        }
        if ($action -in @('keep-local', 'defer')) {
            $oldHash = if ($before.ContainsKey($path)) { $before[$path].candidateSha256 } else { 'MISSING' }
            if ($hash -cne $oldHash) { throw "Retained/deferred path changed without an adopted/adapted disposition: $path" }
        }
    }
    $baseFiles = @(Get-AuditPaths $UpstreamRoot @('ls-tree', '-r', '--name-only', $plan.upstreamRevision) | ForEach-Object { $plan.policy.upstream.localPrefix + $_ })
    foreach ($file in $startSnapshot.files) {
        if ($file -cin $baseFiles -or (Test-ForkPathMatch $file @($plan.policy.forkOwnedPaths))) { continue }
        $retained = $file -cin @($plan.policy.pending | ForEach-Object paths) -or $file -cin @($plan.policy.mirrors.path) -or $file -cin @($plan.policy.adaptations.path)
        if ($retained -and $before.ContainsKey($file) -and (Get-NormalizedSourceHash (Join-Path $root $file)) -ceq $before[$file].candidateSha256) { continue }
        $declared = $dispositions.ContainsKey($file) -and $dispositions[$file] -in @('adopt', 'adapt', 'replace-local')
        $localAdaptation = @($review.batches | Where-Object { $_.action -in @('adapt', 'replace-local') -and $file -cin $_.localPaths }).Count -gt 0
        if (-not $declared -and -not $localAdaptation) { throw "Unclassified result path requires an explicit target/adaptation disposition: $file" }
    }
    foreach ($mirror in $plan.policy.mirrors) {
        $hash = Get-NormalizedSourceHash (Join-Path $root $mirror.path)
        if ($hash -ceq $mirror.sha256) { continue }
        $adapted = @($review.batches | Where-Object { $_.action -in @('adapt', 'replace-local') -and $mirror.path -cin $_.localPaths }).Count -gt 0
        if (-not $adapted -and (-not $dispositions.ContainsKey($mirror.path) -or $dispositions[$mirror.path] -cnotin @('adopt', 'adapt', 'replace-local'))) { throw "Mirror changed without declared disposition: $($mirror.path)" }
    }
    Assert-StableSources
    $results = [ordered]@{ schemaVersion = 3; planInputHash = $plan.planInputHash; decisionHash = $decisionHash
        recordHash = Get-AuditDigest ($review | ConvertTo-Json -Depth 40 -Compress); candidateSnapshot = $startSnapshot; changedPaths = $changedPaths
        deferred = @($review.batches | Where-Object action -eq 'defer' | ForEach-Object {
            [ordered]@{ id = $_.id; paths = @($_.paths); reason = $_.reason; reReviewWhen = $_.reReviewWhen; dependencies = @($_.dependencies) }
        }); policy = $currentPolicy; batches = @($review.batches)
        scope = 'Source provenance and declared results validated. Evidence is reviewer supplied; tests and semantic equivalence are not certified.' }
    $proposal = $currentPolicy | ConvertTo-Json -Depth 40 | ConvertFrom-Json
    $proposal.upstream.revision = $plan.targetRevision
    $proposal.upstream.tag = if ($TargetRef) { $TargetRef } else { $plan.targetRevision }
    $proposal.pending = @($currentPolicy.pending | Where-Object { $null -eq $_.paths }) + @($results.deferred)
    $retainedMappings = @($currentPolicy.adaptations | Where-Object { $null -ne $_ })
    foreach ($path in $plan.requiredPaths) {
        if ($dispositions[$path] -cnotin @('keep-local', 'adapt', 'replace-local') -or $path -cin @($retainedMappings.path) -or
            -not (Test-Path -LiteralPath (Join-Path $root $path) -PathType Leaf)) { continue }
        $relative = $path.Substring($plan.policy.upstream.localPrefix.Length)
        if ((Get-ForkUpstreamHash $UpstreamRoot $plan.targetRevision $relative) -cne 'MISSING') { continue }
        if ((Get-ForkUpstreamHash $UpstreamRoot $plan.upstreamRevision $relative) -cne 'MISSING') {
            $retainedMappings += [ordered]@{ path = $path; upstreamPath = $path; revision = $plan.upstreamRevision }
        }
    }
    if ($retainedMappings.Count) { $proposal | Add-Member -Force NoteProperty adaptations $retainedMappings }
    $proposal.mirrors = @(foreach ($mirror in $currentPolicy.mirrors) {
        $hash = Get-NormalizedSourceHash (Join-Path $root $mirror.path)
        $targetHash = Get-ForkUpstreamHash $UpstreamRoot $plan.targetRevision $mirror.path.Substring($plan.policy.upstream.localPrefix.Length)
        if ($hash -ceq $targetHash -and $hash -cne 'MISSING') { [ordered]@{ path = $mirror.path; sha256 = $hash } }
        elseif ($hash -ceq $mirror.sha256) {
            [ordered]@{ path = $mirror.path; sha256 = $mirror.sha256; revision = if ($mirror.revision) { $mirror.revision } else { $plan.upstreamRevision } }
        }
        # Explicitly adapted former mirrors cease to be mirrors; their contracts remain required.
    })
    $results.policyProposal = $proposal
    $results.resultHash = Get-PlanHash $results 'resultHash'
    Write-ReviewJson 'results.json' $results
    Write-AuditOutput (Join-Path $OutputDirectory 'report.md') (New-ReviewReport $plan $review $results)
    Write-Host "Immutable results recorded; $($results.deferred.Count) batches must be carried into ownership.pending before baseline closure."
    return
}
# Close permits only the reviewed baseline, pending work and source provenance updates.
$results = Read-ReviewJson $resultsPath
if ($results.resultHash -cne (Get-PlanHash $results 'resultHash')) { throw 'Immutable result evidence was modified.' }
if ($results.planInputHash -cne $plan.planInputHash -or $results.decisionHash -cne $decisionHash -or
    $results.recordHash -cne (Get-AuditDigest ($review | ConvertTo-Json -Depth 40 -Compress))) { throw 'Completed result does not match this plan and decision record.' }
if ($currentPolicy.upstream.revision -cne $plan.targetRevision) { throw 'Close requires the comparison baseline advanced to the reviewed target.' }
$policyPath = 'docs/upstream/ownership.json'
$oldManifest = @($results.candidateSnapshot.manifest | Where-Object path -cne $policyPath | ForEach-Object { "$($_.path)`t$($_.sha256)" })
$newManifest = @($startSnapshot.manifest | Where-Object path -cne $policyPath | ForEach-Object { "$($_.path)`t$($_.sha256)" })
if ($startSnapshot.head -cne $results.candidateSnapshot.head -or ($oldManifest -join "`n") -cne ($newManifest -join "`n")) { throw 'Result source changed after validation; baseline closure cannot reuse old results.' }
foreach ($value in @($results.policy, $currentPolicy)) {
    $copy = $value | ConvertTo-Json -Depth 40 | ConvertFrom-Json
    foreach ($field in @('upstream', 'pending', 'mirrors', 'adaptations')) { $copy.PSObject.Properties.Remove($field) }
    $canonical = $copy | ConvertTo-Json -Depth 40 -Compress
    if ($value -eq $results.policy) { $oldPolicy = $canonical } elseif ($canonical -cne $oldPolicy) { throw 'Only baseline, pending and reviewed source provenance may change after Result.' }
}
if ((ConvertTo-Json -InputObject @($currentPolicy.adaptations | Where-Object { $null -ne $_ }) -Depth 20 -Compress) -cne
    (ConvertTo-Json -InputObject @($results.policyProposal.adaptations | Where-Object { $null -ne $_ }) -Depth 20 -Compress)) { throw 'Retained source mappings must match the reviewed policy proposal.' }
if ($currentPolicy.upstream.repository -cne $plan.policy.upstream.repository -or $currentPolicy.upstream.localPrefix -cne $plan.policy.upstream.localPrefix) { throw 'Baseline closure cannot change upstream repository or local prefix.' }
foreach ($pending in $results.deferred) {
    $found = @($currentPolicy.pending | Where-Object id -ceq $pending.id)
    if ($found.Count -ne 1 -or ($found[0] | ConvertTo-Json -Depth 20 -Compress) -cne ($pending | ConvertTo-Json -Depth 20 -Compress)) { throw "Deferred batch must be preserved in durable ownership.pending: $($pending.id)" }
}
foreach ($pending in $plan.pendingDecisions | Where-Object { $null -eq $_.paths }) {
    $found = @($currentPolicy.pending | Where-Object id -ceq $pending.id)
    if ($found.Count -ne 1 -or ($found[0] | ConvertTo-Json -Depth 20 -Compress) -cne ($pending | ConvertTo-Json -Depth 20 -Compress)) { throw "Existing pending decision cannot disappear during baseline closure: $($pending.id)" }
}
foreach ($mirror in $results.policyProposal.mirrors) {
    if ($mirror.path -cnotin @($currentPolicy.mirrors.path)) { throw "Reviewed mirror cannot disappear during baseline closure: $($mirror.path)" }
}
foreach ($mirror in $currentPolicy.mirrors) {
    $sourceRevision = if ($mirror.revision) { $mirror.revision } else { $currentPolicy.upstream.revision }
    if ($sourceRevision -cne $plan.targetRevision) {
        $original = @($plan.policy.mirrors | Where-Object path -ceq $mirror.path)
        $originalRevision = if ($original.Count -and $original[0].revision) { $original[0].revision } else { $plan.upstreamRevision }
        if ($original.Count -ne 1 -or $sourceRevision -cne $originalRevision -or $mirror.sha256 -cne $original[0].sha256 -or
            ($dispositions.ContainsKey($mirror.path) -and $dispositions[$mirror.path] -cnotin @('keep-local', 'defer'))) { throw "Retained mirror must retain its original reviewed provenance: $($mirror.path)" }
    }
}
Test-ForkMirrors -Root $root -Policy $currentPolicy -UpstreamRoot $UpstreamRoot
Assert-StableSources
$closure = [ordered]@{ schemaVersion = 3; planInputHash = $plan.planInputHash; resultSourceHash = $results.candidateSnapshot.hash
    closedSourceHash = $startSnapshot.hash; comparisonBaseline = $plan.targetRevision; deferredIds = @($results.deferred.id)
    scope = 'Comparison baseline reviewed; this does not claim that all target features were adopted.' }
$closurePath = Join-Path $OutputDirectory 'closure.json'
if (Test-Path -LiteralPath $closurePath) {
    $saved = Read-ReviewJson $closurePath
    if (($saved | ConvertTo-Json -Depth 40 -Compress) -cne ($closure | ConvertTo-Json -Depth 40 -Compress)) { throw 'Immutable closure already exists for different inputs.' }
}
else { Write-ReviewJson 'closure.json' $closure }
Write-Host 'Comparison baseline closed with durable deferred decisions and verified mirror provenance; no full-target adoption claim.'
