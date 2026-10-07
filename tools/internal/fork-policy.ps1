#requires -Version 7.0
. (Join-Path $PSScriptRoot 'source-fingerprint.ps1')

function Read-ForkPolicy {
    param([string]$Root, [string]$Path = (Join-Path $Root 'docs/upstream/local-changes.json'))
    $policy = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    if ($policy.schemaVersion -ne 1) { throw 'Unsupported fork policy version.' }
    foreach ($baseline in @($policy.upstream, $policy.reference)) {
        if ($baseline.revision -cnotmatch '^[0-9a-f]{40}$' -or -not $baseline.tag) {
            throw 'Fork baselines require a tag and an exact commit SHA.'
        }
    }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($change in $policy.changes) {
        if ($change.id -cnotmatch '^[a-z][a-z0-9-]+$' -or -not $ids.Add($change.id)) {
            throw "Invalid or duplicate change ID: $($change.id)"
        }
        if ($change.decision -notin @('keep-behavior', 'replaceable', 'product-review') -or
            -not $change.origin -or -not $change.intent -or -not $change.evidence.Count -or
            -not $change.paths.Count -or -not $change.checks.Count) {
            throw "Incomplete change decision: $($change.id)"
        }
        foreach ($pattern in @($change.paths) + @($change.watch) + @($change.checks)) {
            if (-not $pattern -or $pattern.StartsWith('/') -or $pattern -match '\\|:|(^|/)\.\.(/|$)|[\r\n]') {
                throw "Use repository-relative slash paths in fork policy: $pattern"
            }
        }
        foreach ($check in $change.checks) {
            if (-not (Test-Path -LiteralPath (Join-Path $Root $check) -PathType Leaf)) {
                throw "Missing check referenced by $($change.id): $check"
            }
        }
    }
    if (-not $ids.Count) { throw 'Fork policy has no change groups.' }
    foreach ($item in @($policy.pending) + @($policy.knownGaps)) {
        if (-not $item.id -or $item.change -notin @($policy.changes.id) -or -not $ids.Add($item.id)) {
            throw "Invalid decision/gap reference: $($item.id)"
        }
    }
    foreach ($item in $policy.pending) {
        if (-not $item.question) { throw "Pending decision has no question: $($item.id)" }
    }
    foreach ($item in $policy.knownGaps) {
        if (-not $item.expected -or -not $item.observed -or -not $item.coverage) {
            throw "Known gap has no expected/observed/coverage boundary: $($item.id)"
        }
    }
    $mirrors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($mirror in $policy.mirrors) {
        if ($mirror.path -notmatch '^dashboard-src/[A-Za-z0-9_./-]+$' -or
            $mirror.path -match '(^|/)\.\.(/|$)' -or -not $mirrors.Add($mirror.path) -or
            $mirror.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid upstream mirror entry.' }
    }
    return $policy
}

function Test-ForkMirrors {
    param([string]$Root, $Policy)
    foreach ($mirror in $Policy.mirrors) {
        if ((Get-NormalizedSourceHash (Join-Path $Root $mirror.path)) -cne $mirror.sha256) {
            throw "Upstream mirror changed: $($mirror.path). Adapt its consumer/desktop layer, or explicitly review and record a local patch; do not refresh this hash from the working copy."
        }
    }
}

function Get-ForkChangeIds {
    param([string]$Path, $Policy, [switch]$IncludeWatch)
    # PowerShell wildcard semantics: * also spans directories. Keep root config
    # filenames explicit; unknown features must not disappear into a *.ts catch-all.
    foreach ($change in $Policy.changes) {
        $patterns = @($change.paths)
        if ($IncludeWatch) { $patterns += @($change.watch) }
        foreach ($pattern in $patterns) {
            if ([Management.Automation.WildcardPattern]::new($pattern, 'IgnoreCase').IsMatch($Path)) {
                $change.id
                break
            }
        }
    }
}

function Get-ForkRelation {
    param([string]$Before, [string]$After)
    if ($Before -ceq $After) { return 'same' }
    if ($Before -eq 'MISSING') { return 'added' }
    if ($After -eq 'MISSING') { return 'removed' }
    return 'modified'
}
