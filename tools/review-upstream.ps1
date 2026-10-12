#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$UpstreamRoot,
    [string]$TargetRef = '',
    [string]$OutputDirectory = '',
    [string]$DecisionFile = '',
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [ValidateSet('Plan', 'Review', 'Prepare', 'Complete')][string]$Phase = 'Plan',
    [string[]]$Revisit = @(),
    [switch]$AdvanceBaseline
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
function Copy-AuditRecord { param($Value); return ,(ConvertFrom-Json (ConvertTo-Json -InputObject $Value -Depth 60)) }
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
function Assert-NoAuditLinks {
    param([string]$Path)
    for ($ancestor = $Path; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        try { $attributes = [IO.File]::GetAttributes($ancestor) }
        catch [IO.FileNotFoundException] { continue }
        catch [IO.DirectoryNotFoundException] { continue }
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reports and decision files must not use reparse points or symlink ancestors: $ancestor" }
    }
}
function Assert-AuditOutputPath {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.Equals($UpstreamRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($UpstreamRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Reports and decision files must be outside the read-only upstream checkout.'
    }
    Assert-NoAuditLinks $full
    $relative = [IO.Path]::GetRelativePath($root, $full).Replace('\', '/')
    if ($relative -eq '.' -or ($relative -notmatch '^\.\./' -and -not [IO.Path]::IsPathRooted($relative))) {
        $null = git --no-replace-objects -C $root check-ignore --no-index --quiet -- $relative
        if ($LASTEXITCODE -ne 0) { throw 'Reports and decision files inside the candidate must be in ignored output paths, such as .tmp/reports; source files are read-only.' }
    }
    foreach ($file in $startSnapshot.files) {
        if ($full.Equals([IO.Path]::GetFullPath((Join-Path $root $file)), [StringComparison]::OrdinalIgnoreCase)) { throw 'Output includes git-visible source inputs.' }
    }
}
function Write-AuditOutput {
    param([string]$Path, [string]$Content)
    Assert-AuditOutputPath $Path
    $temporary = Join-Path ([IO.Path]::GetDirectoryName($Path)) ('.audit-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, $Content + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
        Assert-AuditOutputPath $Path
        # Replace the directory entry, so an existing hardlink cannot overwrite source.
        [IO.File]::Move($temporary, $Path, $true)
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}
function Write-ReviewJson { param([string]$Name, $Value); Write-AuditOutput (Join-Path $OutputDirectory $Name) (ConvertTo-Json -InputObject $Value -Depth 60) }
function Save-Decisions { Write-AuditOutput $DecisionFile (ConvertTo-Json -InputObject $review -Depth 60) }
function Read-ReviewJson { param([string]$Path); Assert-AuditOutputPath $Path; return [IO.File]::ReadAllText($Path) | ConvertFrom-Json }
function Assert-AuditGitMetadata {
    param([string]$Directory)
    $grafts = [string](Invoke-AuditGit $Directory @('rev-parse', '--git-path', 'info/grafts'))
    if (-not [IO.Path]::IsPathRooted($grafts)) { $grafts = Join-Path $Directory $grafts }
    if (Test-Path -LiteralPath $grafts) { throw 'Git graft metadata is unsupported.' }
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
    $copy = Copy-AuditRecord $Value
    $copy.PSObject.Properties.Remove($HashField)
    return Get-AuditDigest (ConvertTo-Json -InputObject $copy -Depth 60 -Compress)
}
function Get-DecisionHash {
    param($Value)
    # Validation and preview history do not change the implementation decision.
    $copy = Copy-AuditRecord $Value
    foreach ($field in @('review', 'evidence', 'history')) { $copy.PSObject.Properties.Remove($field) }
    return Get-AuditDigest (ConvertTo-Json -InputObject $copy -Depth 60 -Compress)
}
function Get-TargetDiagnostics {
    param($Policy, [string]$Target, [string]$Base)
    $prefix = $Policy.upstream.localPrefix
    $basePaths = @(Get-AuditPaths $UpstreamRoot @('ls-tree', '-r', '--name-only', $Base))
    foreach ($file in @(Get-AuditPaths $UpstreamRoot @('ls-tree', '-r', '--name-only', $Target))) {
        $path = $prefix + $file
        if (Test-ForkPathMatch $path @($Policy.forkOwnedPaths)) { [ordered]@{ path = $path; kind = 'fork-owned-collision' } }
        if ($file -cnotin $basePaths) {
            if (-not @(Get-ForkChangeIds $path $Policy).Count) { [ordered]@{ path = $path; kind = 'target-new-file-needs-contract' } }
            if (Test-Path -LiteralPath (Join-Path $root $path)) { [ordered]@{ path = $path; kind = 'target-new-file-local-overlap' } }
        }
    }
}
function Test-ReviewBatches {
    param($Plan, $Review, [switch]$RequireResolved)
    if ($Review.schemaVersion -ne 4 -or $Review.upstreamRevision -cne $Plan.upstreamRevision -or
        $Review.targetRevision -cne $Plan.targetRevision -or $Review.planInputHash -cne $Plan.planInputHash) { throw 'Decision record does not match the immutable plan.' }
    Assert-ForkArray $Review.batches 'batches'
    Assert-ForkArray $Review.overlapExceptions 'overlapExceptions'
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $coverage = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($batch in $Review.batches) {
        Assert-ForkText $batch.id 'batch.id'
        if (-not $ids.Add($batch.id)) { throw "Duplicate batch ID: $($batch.id)" }
        if ($batch.action -cnotin @('adopt', 'adapt', 'replace-local', 'keep-local', 'defer', 'pending', 'undecided')) { throw "Invalid batch action: $($batch.id)" }
        $question = @($Plan.pendingDecisions | Where-Object { $_.id -ceq $batch.id -and $null -eq $_.paths })
        Assert-ForkArray $batch.paths 'batch.paths' -NonEmpty:($question.Count -eq 0) -Strings
        $localPaths = @()
        if ($null -ne $batch.localPaths) {
            Assert-ForkArray $batch.localPaths 'batch.localPaths' -Strings
            $localPaths = @($batch.localPaths)
        }
        if ($question.Count) {
            # A pathless question is resolved by choosing adoption or retaining local behavior.
            if ($batch.action -cnotin @('pending', 'undecided', 'keep-local', 'adopt', 'defer')) { throw "Question action must be pending, keep-local, adopt or defer: $($batch.id)" }
            if ($batch.action -in @('keep-local', 'adopt', 'defer')) { Assert-ForkText $batch.reason 'question decision reason' }
        }
        if ($batch.action -eq 'defer') { Assert-ForkText $batch.reason 'batch.reason'; Assert-ForkText $batch.reReviewWhen 'batch.reReviewWhen' }
        if ($null -ne $batch.dependencies) { Assert-ForkArray $batch.dependencies 'batch.dependencies' -Strings }
        foreach ($path in @($batch.paths) + $localPaths) {
            Assert-ForkRelativePath $path
            if (-not $coverage.ContainsKey($path)) { $coverage[$path] = [Collections.Generic.List[object]]::new() }
            $coverage[$path].Add([pscustomobject]@{ id = $batch.id; action = $batch.action; local = $path -cin $localPaths })
        }
    }
    $dispositions = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $exceptions = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($exception in $Review.overlapExceptions) {
        Assert-ForkRelativePath $exception.path
        Assert-ForkText $exception.reason 'exception.reason'
        Assert-ForkArray $exception.batches 'exception.batches' -NonEmpty -Strings
        if (-not $coverage.ContainsKey($exception.path) -or -not $exceptions.Add($exception.path) -or
            (@($exception.batches | Sort-Object -Unique) -join "`n") -cne (@($coverage[$exception.path].id | Sort-Object -Unique) -join "`n") -or
            $exception.action -cnotin @($coverage[$exception.path].action)) { throw "Invalid overlap exception: $($exception.path)" }
        $dispositions[$exception.path] = $exception.action
    }
    foreach ($path in $coverage.Keys) {
        if ($dispositions.ContainsKey($path)) { continue }
        $actions = @($coverage[$path].action | Sort-Object -Unique)
        # A declared local adaptation also covers that local incoming name.
        $localActions = @($coverage[$path] | Where-Object { $_.local -and $_.action -in @('adapt', 'replace-local') } | ForEach-Object action | Sort-Object -Unique)
        if ($actions.Count -eq 1) { $dispositions[$path] = $actions[0] }
        elseif ($localActions.Count -eq 1 -and -not @($actions | Where-Object { $_ -notin @('keep-local', 'defer', $localActions[0]) }).Count) { $dispositions[$path] = $localActions[0] }
        else { throw "Batch action conflict requires an explicit scope exception: $path" }
    }
    if ($RequireResolved) {
        if (@($Review.batches | Where-Object { $_.action -in @('pending', 'undecided') }).Count) { throw 'Unresolved user decisions prevent Prepare/Complete.' }
        foreach ($path in $Plan.requiredPaths) { if (-not $dispositions.ContainsKey($path)) { throw "Path requires batch coverage: $path" } }
        foreach ($item in $Plan.triggeredPending | Where-Object { $_.id -cin @($Plan.pendingDecisions | Where-Object { $null -eq $_.paths } | ForEach-Object id) }) {
            if ($item.id -cnotin @($Review.batches.id)) { throw "Triggered question requires an explicit same-ID decision: $($item.id)" }
        }
    }
    return ,$dispositions
}
function Save-DecisionHistory {
    $last = @($review.history | Select-Object -Last 1)
    if ($last.Count -and $last[0].decisionHash -ceq $decisionHash) { return }
    if ($last.Count) { Assert-ForkText $review.revisionReason 'revisionReason for changed decisions' }
    $summaries = @(foreach ($batch in $review.batches) {
        [ordered]@{ id = $batch.id; action = $batch.action; reason = $batch.reason; paths = $batch.paths.Count; localPaths = $(if ($null -eq $batch.localPaths) { 0 } else { $batch.localPaths.Count })
            hash = Get-AuditDigest (ConvertTo-Json -InputObject $batch -Depth 40 -Compress) }
    })
    $review.history = @($review.history) + @([ordered]@{ decisionHash = $decisionHash; reason = $review.revisionReason; batches = $summaries })
}
function New-ReviewReport {
    param($Plan, $Review, $Prepared = $null, $Results = $null)
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('# Upstream follow-up')
    $lines.Add("`nU0: $($Plan.upstreamRevision); U1: $($Plan.targetRevision)")
    $newIncoming = @($Plan.incoming.path | Where-Object { $_ -cnotin @($Plan.pendingDecisions | ForEach-Object paths) })
    $lines.Add("Incoming: $($Plan.incoming.Count) paths ($($newIncoming.Count) new to pending); triggered pending: $($Plan.triggeredPending.Count); automatically carried: $($Plan.pendingDecisions.Count - $Plan.triggeredPending.Count). Only triggered scope requires review.")
    $categories = [ordered]@{ 'Direct adoption / adaptation' = @('adopt', 'adapt', 'replace-local'); 'User decisions needed' = @('pending', 'undecided'); 'Equivalent / retained decisions' = @('keep-local'); 'Deferred work' = @('defer') }
    foreach ($label in $categories.Keys) {
        $batches = @($Review.batches | Where-Object action -cin $categories[$label])
        $missing = if ($label -eq 'User decisions needed') { @($Plan.requiredPaths | Where-Object { $_ -cnotin @($Review.batches | ForEach-Object paths) -and $_ -cnotin @($Review.batches | ForEach-Object localPaths) }) } else { @() }
        $lines.Add("`n## $label ($($batches.Count))")
        foreach ($batch in $batches) {
            $scopePaths = @((@($batch.paths) + @($batch.localPaths)) | Where-Object { $null -ne $_ } | Sort-Object -Unique)
            $lines.Add("- **$($batch.id)** — $($batch.action), $($scopePaths.Count) scope paths. $($batch.reason)")
            foreach ($field in @('impact', 'options', 'recommendation', 'reReviewWhen')) { if ($batch.$field) { $lines.Add("  ${field}: $($batch.$field)") } }
        }
        if ($missing.Count) { $lines.Add("- $($missing.Count) paths have no decision yet; empty batches do not imply defer.") }
        if ($label -eq 'User decisions needed') {
            foreach ($item in $Plan.triggeredPending | Where-Object { $_.id -cnotin @($Review.batches.id) -and $_.id -cin @($Plan.pendingDecisions | Where-Object { $null -eq $_.paths } | ForEach-Object id) }) {
                $lines.Add("- **$($item.id)** — triggered question requires a same-ID decision. $($item.question)")
            }
        }
    }
    if ($Review.history.Count -gt 1) {
        $previous = $Review.history[-2]
        $changed = @($Review.batches | Where-Object { $batch = $_; $old = @($previous.batches | Where-Object id -ceq $batch.id); -not $old.Count -or (Get-AuditDigest (ConvertTo-Json -InputObject $batch -Depth 40 -Compress)) -cne $old[0].hash } | ForEach-Object id)
        $removed = @($previous.batches.id | Where-Object { $_ -cnotin @($Review.batches.id) })
        $lines.Add("`nRevision: $($Review.history[-1].reason). Changed/new: $($changed -join ', '); removed: $($removed -join ', ').")
    }
    if ($Prepared) { $lines.Add("`nCandidate registered before validation: $($Prepared.candidateSnapshot.hash); baseline advanced: $($Prepared.advanceBaseline).") }
    if ($Results) { $lines.Add("`nActual review: $($Results.review)"); foreach ($item in $Results.evidence) { $lines.Add("- $item") } }
    $lines.Add("`n<details><summary>Paths, diagnostics captured at Plan and Git hints</summary>`n")
    foreach ($item in $Plan.targetDiagnostics) { $lines.Add("- $($item.kind): $($item.path)") }
    foreach ($item in $Plan.triggeredPending) { $lines.Add("- Triggered $($item.id): $($item.paths.Count) paths. $($item.reason)$($item.question)") }
    foreach ($batch in $Review.batches) {
        $scopePaths = @((@($batch.paths) + @($batch.localPaths)) | Where-Object { $null -ne $_ } | Sort-Object -Unique)
        $lines.Add("- $($batch.id): $($scopePaths -join ', ')")
    }
    foreach ($hint in @($Plan.renameHints) + @($Plan.commitHints)) { $lines.Add("- $hint") }
    $lines.Add("`nChecks suggested (not executed): $($Plan.checks -join ', ')")
    $lines.Add("`n</details>`n`nRecords describe decisions and supplied evidence. The script checks source binding and provenance; it does not grant user approval or certify semantic equivalence.")
    return $lines -join [Environment]::NewLine
}
function Get-ChangedPaths {
    param($Before, $After, $Dispositions)
    $old = @{}; $new = @{}
    foreach ($file in $Before.manifest) { $old[$file.path] = $file.sha256 }
    foreach ($file in $After.manifest) { $new[$file.path] = $file.sha256 }
    foreach ($path in @(@($old.Keys) + @($new.Keys) | Sort-Object -Unique)) {
        if ($old[$path] -ceq $new[$path]) { continue }
        # Candidate registration itself is an explicit Prepare responsibility.
        if ($path -cne 'docs/upstream/ownership.json' -and (-not $Dispositions.ContainsKey($path) -or $Dispositions[$path] -cnotin @('adopt', 'adapt', 'replace-local'))) { throw "Actual source change is outside adopted/adapted batch paths/localPaths: $path" }
        [ordered]@{ path = $path; beforeSha256 = $old[$path]; afterSha256 = $new[$path] }
    }
}
function Get-CandidatePolicy {
    param($Plan, $Review, $Dispositions, [bool]$Advance)
    $proposal = Copy-AuditRecord $currentPolicy
    $prefix = $Plan.policy.upstream.localPrefix
    if ($proposal.upstream.repository -cne $Plan.policy.upstream.repository -or $proposal.upstream.localPrefix -cne $prefix -or
        $proposal.upstream.revision -cnotin @($Plan.upstreamRevision, $Plan.targetRevision)) { throw 'Candidate policy changed upstream source identity.' }
    if (-not $Advance -and $proposal.upstream.revision -cne $Plan.upstreamRevision) { throw 'Use -AdvanceBaseline consistently for the advanced candidate.' }
    if ($Advance) { $proposal.upstream.revision = $Plan.targetRevision; $proposal.upstream.tag = $Plan.targetRef }
    $pending = [Collections.Generic.List[object]]::new()
    foreach ($item in $Plan.pendingDecisions) {
        $scopeBatches = @($Review.batches | Where-Object id -ceq $item.id)
        if ($null -eq $item.paths) {
            if ($scopeBatches.Count -and $scopeBatches[0].action -in @('keep-local', 'adopt')) { continue }
            $copy = Copy-AuditRecord $item
            if ($scopeBatches.Count -and $scopeBatches[0].action -eq 'defer') {
                foreach ($field in @('reason', 'reReviewWhen', 'dependencies')) { $copy | Add-Member -Force NoteProperty $field $scopeBatches[0].$field }
            }
            $pending.Add($copy)
            continue
        }
        # File disposition authorizes edits, but only this feature's own decision closes its work.
        $completedPaths = @($scopeBatches | Where-Object { $_.action -in @('adopt', 'adapt', 'replace-local', 'keep-local') } | ForEach-Object paths)
        $remaining = @($item.paths | Where-Object { $_ -cnotin $completedPaths })
        if ($remaining.Count) {
            $copy = Copy-AuditRecord $item
            $copy.paths = $remaining
            $updates = @($scopeBatches | Where-Object action -eq 'defer')
            if ($updates.Count -or ($copy.scopeUpdates -and $remaining.Count -ne $item.paths.Count)) {
                $updatedPaths = @($updates | ForEach-Object paths)
                $oldUpdates = @(foreach ($entry in $copy.scopeUpdates) {
                    $scope = @($entry.paths | Where-Object { $_ -cin $remaining -and $_ -cnotin $updatedPaths })
                    if ($scope.Count) { $entryCopy = Copy-AuditRecord $entry; $entryCopy.paths = $scope; $entryCopy }
                })
                $newUpdates = @(foreach ($entry in $updates) {
                    $scope = @($entry.paths | Where-Object { $_ -cin $remaining })
                    if ($scope.Count) { [ordered]@{ paths = $scope; reason = $entry.reason; reReviewWhen = $entry.reReviewWhen; dependencies = @($entry.dependencies) } }
                })
                $copy | Add-Member -Force NoteProperty scopeUpdates @($oldUpdates + $newUpdates)
            }
            $pending.Add($copy)
        }
    }
    foreach ($item in $currentPolicy.pending) {
        if ($item.id -cnotin @($Plan.pendingDecisions.id) -and $item.id -cnotin @($oldPrepared.generatedPendingIds) -and $item.id -cnotin @($Review.batches.id)) { $pending.Add((Copy-AuditRecord $item)) }
    }
    foreach ($batch in $Review.batches | Where-Object action -eq 'defer') {
        if ($batch.id -cin @($Plan.pendingDecisions.id)) {
            $original = @($Plan.pendingDecisions | Where-Object id -ceq $batch.id)[0]
            if ($null -eq $original.paths) { continue }
            $paths = @($batch.paths | Where-Object { $_ -cnotin @($original.paths) })
            if (-not $paths.Count) { continue }
            $existing = @($pending | Where-Object id -ceq $batch.id)[0]
            $existing.paths = @($existing.paths) + $paths
            continue
        }
        $paths = @($batch.paths)
        if (-not $paths.Count) { continue }
        if ($batch.id -cin @($pending.id)) { throw "Deferred batch ID collides with carried pending: $($batch.id)" }
        $pending.Add([ordered]@{ id = $batch.id; paths = $paths; reason = $batch.reason; reReviewWhen = $batch.reReviewWhen; dependencies = @($batch.dependencies) })
    }
    $proposal.pending = @($pending)
    $mappings = @($currentPolicy.adaptations | Where-Object { $null -ne $_ })
    foreach ($mapping in $Plan.policy.adaptations) {
        if ($mapping.path -cnotin @($mappings.path) -and (-not $Dispositions.ContainsKey($mapping.path) -or $Dispositions[$mapping.path] -cnotin @('adopt', 'adapt', 'replace-local'))) { $mappings += Copy-AuditRecord $mapping }
    }
    foreach ($path in $Dispositions.Keys) {
        if (-not $path.StartsWith($prefix, [StringComparison]::Ordinal) -or $path -cin @($mappings.path) -or
            -not (Test-Path -LiteralPath (Join-Path $root $path) -PathType Leaf)) { continue }
        $relative = $path.Substring($prefix.Length)
        $targetSource = Get-ForkUpstreamHash $UpstreamRoot $Plan.targetRevision $relative
        $baseSource = Get-ForkUpstreamHash $UpstreamRoot $Plan.upstreamRevision $relative
        if ($targetSource -eq 'MISSING' -and $baseSource -ne 'MISSING') {
            $mappings += [ordered]@{ path = $path; upstreamPath = $path; revision = $Plan.upstreamRevision }
        }
        elseif (-not $Advance -and $baseSource -eq 'MISSING' -and $targetSource -ne 'MISSING' -and $Dispositions[$path] -in @('adopt', 'adapt', 'replace-local')) {
            # Partial adoption keeps U0 but records where the new U1 file came from.
            $mappings += [ordered]@{ path = $path; upstreamPath = $path; revision = $Plan.targetRevision }
        }
    }
    $proposal | Add-Member -Force NoteProperty adaptations @($mappings)
    foreach ($mapping in $proposal.adaptations) {
        $source = Get-ForkUpstreamHash $UpstreamRoot $mapping.revision $mapping.upstreamPath.Substring($prefix.Length)
        if ($source -eq 'MISSING') { throw "Adaptation upstream source does not exist: $($mapping.upstreamPath)" }
        if (-not @(Get-ForkChangeIds $mapping.path $proposal).Count) { throw "Adaptation requires a contract: $($mapping.path)" }
    }
    # Start with immutable original mirror registrations; deleting one cannot evade provenance checks.
    $mirrors = @($Plan.policy.mirrors) + @($currentPolicy.mirrors | Where-Object path -cnotin @($Plan.policy.mirrors.path))
    $proposal.mirrors = @(foreach ($mirror in $mirrors) {
        $path = $mirror.path
        $hash = Get-NormalizedSourceHash (Join-Path $root $path)
        $revision = if ($mirror.revision) { $mirror.revision } elseif ($mirror.path -cin @($Plan.policy.mirrors.path)) { $Plan.upstreamRevision } else { $currentPolicy.upstream.revision }
        if ((Get-ForkUpstreamHash $UpstreamRoot $revision $path.Substring($prefix.Length)) -cne $mirror.sha256) { throw "Mirror baseline is not the reviewed upstream source: $path" }
        $action = if ($Dispositions.ContainsKey($path)) { $Dispositions[$path] } else { 'keep-local' }
        $targetHash = Get-ForkUpstreamHash $UpstreamRoot $Plan.targetRevision $path.Substring($prefix.Length)
        if ($action -eq 'adopt' -and $hash -ceq $targetHash) {
            if ($hash -ne 'MISSING') { [ordered]@{ path = $path; sha256 = $hash; revision = $Plan.targetRevision } }
        }
        elseif ($action -in @('adapt', 'replace-local')) { }
        elseif ($hash -ceq $mirror.sha256) { [ordered]@{ path = $path; sha256 = $mirror.sha256; revision = $revision } }
        else { throw "Mirror changed without declared disposition: $path" }
    })
    return $proposal
}
function Test-CandidateSources {
    param($Plan, $Dispositions, $Proposal)
    $prefix = $Proposal.upstream.localPrefix
    $before = @{}; foreach ($row in @(Read-ReviewJson (Join-Path $OutputDirectory 'inventory.json'))) { $before[$row.path] = $row.candidateSha256 }
    foreach ($path in $Dispositions.Keys) {
        $action = $Dispositions[$path]
        $hash = Get-NormalizedSourceHash (Join-Path $root $path)
        if ($action -eq 'adopt') {
            if (-not $path.StartsWith($prefix, [StringComparison]::Ordinal) -or $hash -cne (Get-ForkUpstreamHash $UpstreamRoot $Plan.targetRevision $path.Substring($prefix.Length))) { throw "Adopt must match exact target source (including deletions): $path" }
        }
        if ($action -in @('keep-local', 'defer')) {
            $oldHash = if ($before.ContainsKey($path)) { $before[$path] } else { 'MISSING' }
            if ($hash -cne $oldHash) { throw "Retained/deferred path changed without an adapted disposition: $path" }
        }
    }
    $diagnostics = @(Get-TargetDiagnostics $Proposal $Plan.targetRevision $Plan.upstreamRevision)
    foreach ($item in $diagnostics) {
        if ($item.kind -eq 'fork-owned-collision') { throw "Fork-owned path collides with target upstream: $($item.path)" }
        if ($item.kind -eq 'target-new-file-needs-contract' -and ($Proposal.upstream.revision -ceq $Plan.targetRevision -or ($Dispositions.ContainsKey($item.path) -and $Dispositions[$item.path] -in @('adopt', 'adapt', 'replace-local')))) { throw "Target new file requires a contract: $($item.path)" }
    }
    $futureFiles = @(Get-AuditPaths $UpstreamRoot @('ls-tree', '-r', '--name-only', $Proposal.upstream.revision) | ForEach-Object { $prefix + $_ })
    foreach ($file in $startSnapshot.files) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { continue }
        $owned = Test-ForkPathMatch $file @($Proposal.forkOwnedPaths)
        if ($owned) {
            if ($file -cin $futureFiles) { throw "Fork-owned path collides with candidate baseline: $file" }
            continue
        }
        $mapped = $file -cin @($Proposal.adaptations.path) -or $file -cin @($Proposal.mirrors.path) -or $file -cin @($Proposal.pending | ForEach-Object paths)
        $incomingAdopted = $file.StartsWith($prefix, [StringComparison]::Ordinal) -and $Dispositions.ContainsKey($file) -and $Dispositions[$file] -in @('adopt', 'adapt', 'replace-local') -and
            (Get-ForkUpstreamHash $UpstreamRoot $Plan.targetRevision $file.Substring($prefix.Length)) -ne 'MISSING'
        if ($file -cnotin $futureFiles -and -not $mapped -and -not $incomingAdopted) { throw "Unclassified result path requires explicit target/adaptation provenance: $file" }
        $same = $file -cin $futureFiles -and (Get-NormalizedSourceHash (Join-Path $root $file)) -ceq (Get-ForkUpstreamHash $UpstreamRoot $Proposal.upstream.revision $file.Substring($prefix.Length))
        if (-not $same -and -not @(Get-ForkChangeIds $file $Proposal).Count) { throw "Shared candidate path requires a contract: $file" }
    }
    Test-ForkMirrors -Root $root -Policy $Proposal -UpstreamRoot $UpstreamRoot
}

Assert-AuditGitMetadata $root
$startSnapshot = Get-AuditSourceSnapshot
$currentPolicy = Read-ForkPolicy -Root $root
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root '.tmp/reports/upstream-current' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $UpstreamRoot) {
    $existingPlanPath = Join-Path $OutputDirectory 'plan.json'
    $UpstreamRoot = if (Test-Path -LiteralPath $existingPlanPath) { ([IO.File]::ReadAllText($existingPlanPath) | ConvertFrom-Json).upstreamRoot }
        else { Join-Path $root ('.tmp/experiments/upstream-audit-' + $currentPolicy.upstream.tag) }
}
$UpstreamRoot = [IO.Path]::GetFullPath($UpstreamRoot)
$names = @('plan.json', 'inventory.json', 'decisions.json', 'prepared.json', 'results.json', 'report.md')
foreach ($name in $names) { Assert-AuditOutputPath (Join-Path $OutputDirectory $name) }
if (-not $DecisionFile) { $DecisionFile = Join-Path $OutputDirectory 'decisions.json' }
$DecisionFile = [IO.Path]::GetFullPath($DecisionFile)
Assert-AuditOutputPath $DecisionFile
foreach ($name in $names | Where-Object { $_ -ne 'decisions.json' }) { if ($DecisionFile.Equals((Join-Path $OutputDirectory $name), [StringComparison]::OrdinalIgnoreCase)) { throw 'DecisionFile must be the editable decision record, not generated evidence.' } }
Assert-AuditGitMetadata $UpstreamRoot
$checkoutRevision = [string](Invoke-AuditGit $UpstreamRoot @('rev-parse', 'HEAD'))
if (@(Invoke-AuditGit $UpstreamRoot @('status', '--porcelain', '--untracked-files=all')).Count) { throw 'The upstream worktree must be clean.' }
if ($Revisit.Count -and $Phase -ne 'Plan') { throw 'Revisit is only valid in Plan.' }
if ($AdvanceBaseline -and $Phase -ne 'Prepare') { throw 'AdvanceBaseline is only valid in Prepare.' }
$planPath = Join-Path $OutputDirectory 'plan.json'
if ($Phase -eq 'Plan' -and -not (Test-Path -LiteralPath $planPath)) {
    if ($checkoutRevision -cne $currentPolicy.upstream.revision) { throw 'The upstream checkout must be at the exact reviewed policy SHA.' }
    $target = if ($TargetRef) { [string](Invoke-AuditGit $UpstreamRoot @('rev-parse', ($TargetRef + '^{commit}'))) } else { $checkoutRevision }
    $null = Invoke-AuditGit $UpstreamRoot @('merge-base', '--is-ancestor', $checkoutRevision, $target)
    Test-ForkMirrors -Root $root -Policy $currentPolicy -UpstreamRoot $UpstreamRoot
    foreach ($mapping in $currentPolicy.adaptations) { if ((Get-ForkUpstreamHash $UpstreamRoot $mapping.revision $mapping.upstreamPath.Substring($currentPolicy.upstream.localPrefix.Length)) -eq 'MISSING') { throw "Adaptation upstream source does not exist: $($mapping.upstreamPath)" } }
    foreach ($name in $names | Where-Object { $_ -notin @('plan.json', 'report.md') }) { if (Test-Path -LiteralPath (Join-Path $OutputDirectory $name)) { throw 'Existing review evidence without its plan; choose a new output directory.' } }
    if (Test-Path -LiteralPath $DecisionFile) { throw 'Existing decision record without its plan.' }
    foreach ($id in $Revisit) { if ($id -cnotin @($currentPolicy.pending.id)) { throw "Unknown pending ID for Revisit: $id" } }
    $prefix = $currentPolicy.upstream.localPrefix
    $upstreamFiles = @(Get-AuditPaths $UpstreamRoot @('ls-tree', '-r', '--name-only', $checkoutRevision) | ForEach-Object { $prefix + $_ })
    $rows = @(foreach ($file in @($upstreamFiles + $startSnapshot.files | Sort-Object -Unique)) {
        $inUpstream = $file -cin $upstreamFiles
        $owned = Test-ForkPathMatch $file @($currentPolicy.forkOwnedPaths)
        if ($owned -and $inUpstream) { throw "Fork-owned path collides with reviewed upstream: $file" }
        $u = if ($inUpstream) { Get-ForkUpstreamHash $UpstreamRoot $checkoutRevision $file.Substring($prefix.Length) } else { 'MISSING' }
        $d = Get-NormalizedSourceHash (Join-Path $root $file)
        $relation = Get-ForkRelation $u $d
        $retained = $file -cin @($currentPolicy.pending | ForEach-Object paths) -or $file -cin @($currentPolicy.mirrors.path) -or $file -cin @($currentPolicy.adaptations.path)
        [ordered]@{ path = $file; upstreamSha256 = $u; candidateSha256 = $d; upstreamToCandidate = $relation
            ownership = if ($u -eq 'MISSING' -and $d -eq 'MISSING') { 'absent' } elseif ($owned) { 'fork-owned' } elseif (($inUpstream -and $relation -ne 'same') -or $retained) { 'shared' } elseif ($inUpstream) { 'upstream' } else { 'unclassified' }
            contracts = @(Get-ForkChangeIds $file $currentPolicy) }
    })
    $byPath = @{}; foreach ($row in $rows) { $byPath[$row.path] = $row }
    $incoming = @(foreach ($file in @(Get-AuditPaths $UpstreamRoot @('diff', '--name-only', '--no-renames', $checkoutRevision, $target))) {
        $path = $prefix + $file
        $direct = @(Get-ForkChangeIds $path $currentPolicy)
        $groups = @(Get-ForkChangeIds $path $currentPolicy -IncludeWatch)
        $contracts = @($currentPolicy.contracts | Where-Object id -cin $groups)
        $owned = Test-ForkPathMatch $path @($currentPolicy.forkOwnedPaths)
        $overlap = $byPath.ContainsKey($path) -and $byPath[$path].upstreamToCandidate -ne 'same'
        $risk = 'low'; foreach ($contract in $contracts) { if ($contract.risk -eq 'high' -or ($contract.risk -eq 'medium' -and $risk -eq 'low')) { $risk = $contract.risk } }
        if (-not $groups.Count) { $risk = 'high' }
        [ordered]@{ path = $path; localOverlap = $overlap; contracts = $groups; risk = $risk
            attention = if ($owned) { 'fork-owned-collision' } elseif ($overlap) { 'shared-overlap' } elseif ($direct.Count) { 'upstream-change' } elseif ($groups.Count) { 'contract-watch' } else { 'unclassified' }
            targetSha256 = Get-ForkUpstreamHash $UpstreamRoot $target $file }
    })
    $triggered = @(foreach ($item in $currentPolicy.pending) {
        $paths = if ($item.paths) { @($item.paths | Where-Object { $_ -cin @($incoming.path) -or $item.id -cin $Revisit }) }
        else { $contract = @($currentPolicy.contracts | Where-Object id -ceq $item.contract)[0]; @($incoming.path | Where-Object { Test-ForkPathMatch $_ (@($contract.paths) + @($contract.watch)) }) }
        if ($paths.Count -or $item.id -cin $Revisit) { $copy = Copy-AuditRecord $item; $copy | Add-Member -Force NoteProperty paths @($paths); $copy }
    })
    $required = @(@($incoming.path) + @($triggered | ForEach-Object paths) | Sort-Object -Unique)
    $affectedIds = @($required | ForEach-Object { Get-ForkChangeIds $_ $currentPolicy -IncludeWatch } | Sort-Object -Unique)
    $inventoryText = (ConvertTo-Json -InputObject $rows -Depth 40) + [Environment]::NewLine
    $plan = [ordered]@{ schemaVersion = 4; upstreamRevision = $checkoutRevision; targetRevision = $target; targetRef = if ($TargetRef) { $TargetRef } else { $target }; upstreamRoot = $UpstreamRoot
        candidateSnapshot = $startSnapshot; candidateDirty = (@(Invoke-AuditGit $root @('status', '--porcelain')).Count -gt 0); policy = $currentPolicy
        inventoryHash = Get-AuditDigest $inventoryText; incoming = $incoming; requiredPaths = $required; pendingDecisions = @($currentPolicy.pending); triggeredPending = $triggered; revisit = $Revisit
        targetDiagnostics = @(Get-TargetDiagnostics $currentPolicy $target $checkoutRevision)
        affectedContracts = @($currentPolicy.contracts | Where-Object id -cin $affectedIds | ForEach-Object { [ordered]@{ id = $_.id; risk = $_.risk; rule = $_.rule } })
        checks = @($currentPolicy.contracts | Where-Object id -cin $affectedIds | ForEach-Object checks | Sort-Object -Unique)
        renameHints = @(Invoke-AuditGit $UpstreamRoot @('diff', '--name-status', '-M', $checkoutRevision, $target) | Where-Object { $_ -cmatch '^R[0-9]+\s' })
        commitHints = @(Invoke-AuditGit $UpstreamRoot @('log', '--format=%H %s', '--reverse', ($checkoutRevision + '..' + $target))) }
    $plan.planInputHash = Get-PlanHash $plan
    $review = [ordered]@{ schemaVersion = 4; upstreamRevision = $checkoutRevision; targetRevision = $target; planInputHash = $plan.planInputHash
        batches = @(); overlapExceptions = @(); revisionReason = ''; history = @(); review = ''; evidence = @() }
    Assert-StableSources
    $null = New-Item -ItemType Directory -Force $OutputDirectory
    $null = New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($DecisionFile))
    Write-AuditOutput (Join-Path $OutputDirectory 'inventory.json') $inventoryText.TrimEnd("`r", "`n")
    Write-ReviewJson 'plan.json' $plan
    Save-Decisions
    Write-AuditOutput (Join-Path $OutputDirectory 'report.md') (New-ReviewReport $plan $review)
    Write-Host "Plan fixed: $($incoming.Count) incoming; $($triggered.Count) triggered pending; $($currentPolicy.pending.Count - $triggered.Count) automatically carried. Edit $DecisionFile"
    return
}
$plan = Read-ReviewJson $planPath
if ($plan.schemaVersion -ne 4 -or $plan.planInputHash -cne (Get-PlanHash $plan) -or
    $plan.inventoryHash -cne (Get-AuditDigest ([IO.File]::ReadAllText((Join-Path $OutputDirectory 'inventory.json'))))) { throw 'Immutable plan evidence was modified.' }
if ($TargetRef -and [string](Invoke-AuditGit $UpstreamRoot @('rev-parse', ($TargetRef + '^{commit}'))) -cne $plan.targetRevision) { throw 'TargetRef differs from immutable plan.' }
if ($checkoutRevision -cnotin @($plan.upstreamRevision, $plan.targetRevision)) { throw 'Upstream checkout must remain at U0 or U1; target blobs are read without checkout.' }
if ($Phase -eq 'Plan') {
    if ($Revisit.Count -and (@($Revisit | Sort-Object -Unique) -join "`n") -cne (@($plan.revisit | Sort-Object -Unique) -join "`n")) { throw 'Revisit scope differs from immutable plan; choose a new round.' }
    Assert-StableSources
    Write-Host 'Original plan preserved; implementation and decision revisions do not require replanning.'
    return
}
$review = Read-ReviewJson $DecisionFile
$dispositions = Test-ReviewBatches $plan $review -RequireResolved:($Phase -in @('Prepare', 'Complete'))
$decisionHash = Get-DecisionHash $review
$preparedPath = Join-Path $OutputDirectory 'prepared.json'
$resultsPath = Join-Path $OutputDirectory 'results.json'
if (Test-Path -LiteralPath $resultsPath) {
    $results = Read-ReviewJson $resultsPath
    if ($results.resultHash -cne (Get-PlanHash $results 'resultHash') -or $results.planInputHash -cne $plan.planInputHash -or
        $results.decisionHash -cne $decisionHash -or $results.candidateSnapshot.hash -cne $startSnapshot.hash -or
        $results.recordHash -cne (Get-AuditDigest (ConvertTo-Json -InputObject $review -Depth 60 -Compress))) { throw 'Completed round is immutable; changed source/decisions/evidence require a new round.' }
    Assert-StableSources
    Write-Host 'Existing completion preserved; no further registration or source changes.'
    return
}
if ($Phase -eq 'Review') {
    Save-DecisionHistory
    Assert-StableSources
    Save-Decisions
    Write-AuditOutput (Join-Path $OutputDirectory 'report.md') (New-ReviewReport $plan $review)
    Write-Host 'Current decision preview refreshed; no approval or implementation gate.'
    return
}
if ($Phase -eq 'Prepare') {
    $oldPrepared = if (Test-Path -LiteralPath $preparedPath) { Read-ReviewJson $preparedPath } else { $null }
    if ($oldPrepared -and $oldPrepared.preparedHash -cne (Get-PlanHash $oldPrepared 'preparedHash')) { throw 'Prepared evidence was modified.' }
    $advance = [bool]$AdvanceBaseline
    if ($oldPrepared -and $oldPrepared.advanceBaseline -and $currentPolicy.upstream.revision -ceq $plan.targetRevision) { $advance = $true }
    $proposal = Get-CandidatePolicy $plan $review $dispositions $advance
    $changed = @(Get-ChangedPaths $plan.candidateSnapshot $startSnapshot $dispositions)
    Test-CandidateSources $plan $dispositions $proposal
    Save-DecisionHistory
    Assert-StableSources
    $policyPath = Join-Path $root 'docs/upstream/ownership.json'
    if ($policyPath.StartsWith($UpstreamRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate registration must not write upstream source.' }
    Assert-NoAuditLinks $policyPath
    if ((ConvertTo-Json -InputObject $proposal -Depth 60 -Compress) -cne (ConvertTo-Json -InputObject $currentPolicy -Depth 60 -Compress)) {
        $temporary = Join-Path ([IO.Path]::GetDirectoryName($policyPath)) ('.ownership-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        try { [IO.File]::WriteAllText($temporary, (ConvertTo-Json -InputObject $proposal -Depth 60) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)); [IO.File]::Move($temporary, $policyPath, $true) }
        finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
    }
    $registeredPolicy = Read-ForkPolicy -Root $root
    if ((ConvertTo-Json -InputObject $registeredPolicy -Depth 60 -Compress) -cne (ConvertTo-Json -InputObject $proposal -Depth 60 -Compress)) { throw 'Candidate policy changed during registration.' }
    $beforeRegistration = $startSnapshot
    $startSnapshot = Get-AuditSourceSnapshot
    $registrationChanges = @(Get-ChangedPaths $beforeRegistration $startSnapshot $dispositions | Where-Object path -cne 'docs/upstream/ownership.json')
    if ($beforeRegistration.head -cne $startSnapshot.head -or $registrationChanges.Count) { throw 'Candidate source inputs changed during registration; rerun Prepare with stable inputs.' }
    $changed = @(Get-ChangedPaths $plan.candidateSnapshot $startSnapshot $dispositions)
    $prepared = [ordered]@{ schemaVersion = 4; planInputHash = $plan.planInputHash; decisionHash = $decisionHash; advanceBaseline = $advance; candidateSnapshot = $startSnapshot; changedPaths = $changed; policy = $proposal
        generatedPendingIds = @($review.batches | Where-Object { $_.action -eq 'defer' -and $_.id -cnotin @($plan.pendingDecisions.id) } | ForEach-Object id) }
    $prepared.preparedHash = Get-PlanHash $prepared 'preparedHash'
    if (-not $oldPrepared -or $oldPrepared.preparedHash -cne $prepared.preparedHash) { $review.review = ''; $review.evidence = @() }
    Assert-StableSources
    Save-Decisions
    Write-ReviewJson 'prepared.json' $prepared
    Write-AuditOutput (Join-Path $OutputDirectory 'report.md') (New-ReviewReport $plan $review $prepared)
    Write-Host 'Candidate ownership registered and raw source bound. Run existing validation, fill decisions.review/evidence, then Complete.'
    return
}
$prepared = Read-ReviewJson $preparedPath
if ($prepared.preparedHash -cne (Get-PlanHash $prepared 'preparedHash') -or $prepared.planInputHash -cne $plan.planInputHash) { throw 'Prepared evidence was modified.' }
if ($prepared.decisionHash -cne $decisionHash -or $prepared.candidateSnapshot.hash -cne $startSnapshot.hash) { throw 'Prepared source or decisions changed; rerun Prepare to discard stale evidence before validation.' }
Assert-ForkText $review.review 'review actual implementation assessment'
Assert-ForkArray $review.evidence 'evidence actual round validation' -NonEmpty -Strings
Assert-StableSources
$results = [ordered]@{ schemaVersion = 4; planInputHash = $plan.planInputHash; decisionHash = $decisionHash; preparedHash = $prepared.preparedHash
    recordHash = Get-AuditDigest (ConvertTo-Json -InputObject $review -Depth 60 -Compress); candidateSnapshot = $startSnapshot; changedPaths = $prepared.changedPaths; policy = $currentPolicy
    batches = $review.batches; review = $review.review; evidence = $review.evidence; advanceBaseline = $prepared.advanceBaseline }
$results.resultHash = Get-PlanHash $results 'resultHash'
Write-ReviewJson 'results.json' $results
Write-AuditOutput (Join-Path $OutputDirectory 'report.md') (New-ReviewReport $plan $review $prepared $results)
Write-Host 'Immutable completion recorded against the validated candidate; no Git source changed.'
