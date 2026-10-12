#requires -Version 7.0
. (Join-Path $PSScriptRoot 'source-fingerprint.ps1')

function Assert-ForkText {
    param($Value, [string]$Field)
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { throw "$Field must be a nonblank string." }
}

function Assert-ForkArray {
    param($Value, [string]$Field, [switch]$NonEmpty, [switch]$Strings)
    if ($Value -isnot [array] -or ($NonEmpty -and -not $Value.Count)) { throw "$Field must be an array$($NonEmpty ? ' with at least one entry' : '')." }
    if ($Strings) { foreach ($entry in $Value) { Assert-ForkText $entry "$Field entry" } }
}

function Assert-ForkRelativePath {
    param($Path)
    Assert-ForkText $Path 'Policy path'
    if ($Path.StartsWith('/') -or $Path -match '\\|:|(^|/)\.\.(/|$)|[\r\n]') {
        throw "Use repository-relative slash paths in ownership policy: $Path"
    }
}

function Read-ForkPolicy {
    param([string]$Root, [string]$Path = (Join-Path $Root 'docs/upstream/ownership.json'))
    $policy = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    if ($policy.schemaVersion -ne 2) { throw 'Unsupported ownership policy version.' }
    foreach ($field in @('repository', 'tag', 'revision', 'localPrefix')) { Assert-ForkText $policy.upstream.$field "upstream.$field" }
    if ($policy.upstream.revision -cnotmatch '^[0-9a-f]{40}$' -or $policy.upstream.localPrefix -cne 'dashboard-src/') {
        throw 'Upstream requires repository, tag, exact reviewed SHA and localPrefix dashboard-src/.'
    }
    # Historical reference is documentation only: never resolve or archive it.
    if ($null -ne $policy.historicalReference) {
        foreach ($field in @('tag', 'revision')) { Assert-ForkText $policy.historicalReference.$field "historicalReference.$field" }
        if ($policy.historicalReference.revision -cnotmatch '^[0-9a-f]{40}$') { throw 'Historical reference requires tag and exact SHA when supplied.' }
    }
    foreach ($field in @('forkOwnedPaths', 'mirrors', 'contracts', 'pending', 'knownGaps')) { Assert-ForkArray $policy.$field $field }
    $owned = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($pattern in $policy.forkOwnedPaths) {
        Assert-ForkRelativePath $pattern
        if (-not $owned.Add($pattern)) { throw "Duplicate fork-owned pattern: $pattern" }
        if ($pattern.StartsWith($policy.upstream.localPrefix, [StringComparison]::OrdinalIgnoreCase) -and
            -not $pattern.StartsWith($policy.upstream.localPrefix, [StringComparison]::Ordinal)) {
            throw "Use the canonical dashboard-src/ prefix in ownership paths: $pattern"
        }
        # Only literal files or literal directory namespaces. Audit proves namespaces disjoint from U0.
        if ($pattern -match '[*?\[\]]' -and $pattern -notmatch '^[^*?\[\]]+/\*$') {
            throw "Fork-owned wildcards must be literal directory namespaces ending /*: $pattern"
        }
    }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($contract in $policy.contracts) {
        foreach ($field in @('id', 'risk')) { Assert-ForkText $contract.$field "contract.$field" }
        foreach ($field in @('paths', 'checks')) { Assert-ForkArray $contract.$field "contract.$field" -NonEmpty -Strings }
        Assert-ForkText $contract.rule 'contract.rule'
        if ($contract.rule -cne "docs/maintenance.md#$($contract.id)") { throw "Contract rule must reference docs/maintenance.md#$($contract.id)." }
        $rules = Join-Path $Root 'docs/maintenance.md'
        if (-not (Test-Path -LiteralPath $rules -PathType Leaf) -or
            [IO.File]::ReadAllText($rules) -cnotmatch ('id=["'']' + [regex]::Escape($contract.id) + '["'']')) {
            throw "Missing explicit maintenance rule anchor: $($contract.rule)"
        }
        Assert-ForkArray $contract.watch 'contract.watch' -Strings
        if ($contract.id -cnotmatch '^[a-z][a-z0-9-]+$' -or -not $ids.Add($contract.id)) {
            throw "Invalid or duplicate contract ID: $($contract.id)"
        }
        if ($contract.risk -cnotin @('low', 'medium', 'high')) {
            throw "Incomplete contract: $($contract.id)"
        }
        foreach ($pattern in @($contract.paths) + @($contract.watch) + @($contract.checks)) { Assert-ForkRelativePath $pattern }
        foreach ($check in $contract.checks) {
            if (-not (Test-Path -LiteralPath (Join-Path $Root $check) -PathType Leaf)) { throw "Missing check referenced by $($contract.id): $check" }
        }
    }
    if (-not $ids.Count) { throw 'Ownership policy has no contracts.' }
    foreach ($item in @($policy.pending) + @($policy.knownGaps)) {
        Assert-ForkText $item.id 'decision/gap.id'
        if (($null -ne $item.contract -and $item.contract -cnotin @($policy.contracts.id)) -or -not $ids.Add($item.id)) {
            throw "Invalid decision/gap reference: $($item.id)"
        }
    }
    foreach ($item in $policy.pending) {
        if ($null -ne $item.paths) {
            Assert-ForkArray $item.paths 'pending.paths' -NonEmpty -Strings
            foreach ($path in $item.paths) { Assert-ForkRelativePath $path }
            foreach ($field in @('reason', 'reReviewWhen')) { Assert-ForkText $item.$field "pending.$field" }
            Assert-ForkArray $item.dependencies 'pending.dependencies' -Strings
        }
        else {
            Assert-ForkText $item.contract 'pending.contract'
            Assert-ForkText $item.question 'pending.question'
        }
    }
    foreach ($item in $policy.knownGaps) {
        foreach ($field in @('expected', 'observed', 'coverage')) { Assert-ForkText $item.$field "knownGaps.$field" }
    }
    if ($null -ne $policy.adaptations) {
        Assert-ForkArray $policy.adaptations 'adaptations'
        $mapped = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($mapping in $policy.adaptations) {
            foreach ($field in @('path', 'upstreamPath')) {
                Assert-ForkRelativePath $mapping.$field
                if (-not $mapping.$field.StartsWith($policy.upstream.localPrefix, [StringComparison]::Ordinal)) { throw 'Adaptation paths must use the dashboard-src/ prefix.' }
            }
            if (-not $mapped.Add($mapping.path) -or $mapping.revision -cnotmatch '^[0-9a-f]{40}$') { throw 'Adaptation requires a unique local path and exact source revision.' }
        }
    }
    $mirrors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($mirror in $policy.mirrors) {
        Assert-ForkText $mirror.sha256 'mirror.sha256'
        if ($null -ne $mirror.revision -and $mirror.revision -cnotmatch '^[0-9a-f]{40}$') { throw 'Mirror source revision must be an exact SHA.' }
        Assert-ForkRelativePath $mirror.path
        if (-not $mirror.path.StartsWith($policy.upstream.localPrefix) -or -not $mirrors.Add($mirror.path) -or
            $mirror.sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid upstream mirror entry.' }
    }
    return $policy
}

function Get-ForkUpstreamHash {
    param([string]$Root, [string]$Revision, [string]$Path)
    $exists = @(git --no-replace-objects --literal-pathspecs -C $Root ls-tree $Revision -- $Path)
    if ($LASTEXITCODE) { throw "Cannot read upstream tree $Revision." }
    if ($exists.Count -ne 1 -or $exists[0] -cnotmatch '^[0-9]{6} blob [0-9a-f]{40}\t') { return 'MISSING' }
    $info = [Diagnostics.ProcessStartInfo]::new('git')
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in @('--no-replace-objects', '-C', $Root, 'show', ($Revision + ':' + $Path))) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    $stream = [IO.MemoryStream]::new()
    try {
        $errorText = $process.StandardError.ReadToEndAsync()
        $process.StandardOutput.BaseStream.CopyTo($stream)
        $process.WaitForExit()
        if ($process.ExitCode) { throw "Cannot read upstream blob: $($errorText.GetAwaiter().GetResult())" }
        $bytes = $stream.ToArray()
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        if ($text.IndexOf([char]0) -lt 0 -and $text.IndexOf([char]0xfffd) -lt 0) { $bytes = [Text.Encoding]::UTF8.GetBytes($text.Replace("`r`n", "`n")) }
        $hash = [Security.Cryptography.SHA256]::Create()
        try { return [BitConverter]::ToString($hash.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
        finally { $hash.Dispose() }
    }
    finally { $process.Dispose(); $stream.Dispose() }
}

function Test-ForkMirrors {
    param([string]$Root, $Policy, [string]$UpstreamRoot = '')
    foreach ($mirror in $Policy.mirrors) {
        $revision = if ($mirror.revision) { $mirror.revision } else { $Policy.upstream.revision }
        if ($UpstreamRoot -and (Get-ForkUpstreamHash $UpstreamRoot $revision $mirror.path.Substring($Policy.upstream.localPrefix.Length)) -cne $mirror.sha256) {
            throw "Mirror baseline is not the reviewed upstream source: $($mirror.path)"
        }
        if ((Get-NormalizedSourceHash (Join-Path $Root $mirror.path)) -cne $mirror.sha256) {
            throw "Upstream mirror changed: $($mirror.path). Review a local adaptation explicitly; do not refresh this hash from the working copy."
        }
    }
}

function Test-ForkPathMatch {
    param([string]$Path, [object[]]$Patterns)
    foreach ($pattern in $Patterns) {
        # * spans directories, as in the existing register. Frontend ownership forbids wildcards.
        if ([Management.Automation.WildcardPattern]::new($pattern, 'IgnoreCase').IsMatch($Path)) { return $true }
    }
    return $false
}

function Get-ForkChangeIds {
    param([string]$Path, $Policy, [switch]$IncludeWatch)
    foreach ($contract in $Policy.contracts) {
        $patterns = @($contract.paths)
        if ($IncludeWatch) { $patterns += @($contract.watch) }
        if (Test-ForkPathMatch $Path $patterns) { $contract.id }
    }
}

function Get-ForkRelation {
    param([string]$Before, [string]$After)
    if ($Before -ceq $After) { return 'same' }
    if ($Before -eq 'MISSING') { return 'added' }
    if ($After -eq 'MISSING') { return 'removed' }
    return 'modified'
}
