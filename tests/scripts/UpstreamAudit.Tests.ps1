#requires -Version 7.0
# Offline integration fixtures apply real target additions, deletions, renames and mirror updates.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestPaths.ps1')
. (Join-Path $PSScriptRoot '..\..\tools\internal\fork-policy.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$policy = Read-ForkPolicy -Root $repository
Test-ForkMirrors -Root $repository -Policy $policy
$entry = Join-Path $repository 'tools/review-upstream.ps1'
$fixture = New-TemporaryTestDirectory 'upstream-review'
$upstream = Join-Path $fixture 'upstream'
$local = Join-Path $fixture 'candidate'
$output = Join-Path $local '.tmp/reports/review'
function Assert-True {
    param([bool]$Value, [string]$Message)
    if (-not $Value) { throw $Message }
}
function Assert-Fails {
    param([scriptblock]$Action, [string]$MessagePattern)
    $caught = $false
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike $MessagePattern) { throw "Unexpected error (expected $MessagePattern): $($_.Exception.Message)" }
        $caught = $true
    }
    Assert-True $caught "Expected rejection: $MessagePattern"
}
function Invoke-FixtureGit {
    param([string]$Root, [string[]]$Arguments)
    $result = @(git -C $Root @Arguments 2>&1)
    if ($LASTEXITCODE) { throw ($result -join "`n") }
    return $result
}
function Write-FixtureFile {
    param([string]$Root, [string]$Path, [string]$Text)
    $full = Join-Path $Root $Path
    $null = New-Item -ItemType Directory -Force (Split-Path -Parent $full)
    [IO.File]::WriteAllText($full, $Text, [Text.UTF8Encoding]::new($false))
}
function Save-FixturePolicy { Write-FixtureFile $local 'docs/upstream/ownership.json' ($fixturePolicy | ConvertTo-Json -Depth 40) }
function Invoke-Review {
    param([string]$Phase = 'Plan', [switch]$ResetResults)
    & $entry -RepositoryRoot $local -UpstreamRoot $upstream -TargetRef $target -OutputDirectory $output -Phase $Phase -ResetResults:$ResetResults
}
function Read-Record {
    param([string]$Name)
    return [IO.File]::ReadAllText((Join-Path $output $Name)) | ConvertFrom-Json
}
function Save-Decisions { Write-FixtureFile $local ([IO.Path]::GetRelativePath($local, (Join-Path $output 'decisions.json'))) ($decisions | ConvertTo-Json -Depth 40) }
function New-Batch {
    param([string]$Id, [string[]]$Paths, [string]$Action)
    return [pscustomobject]@{ id = $Id; paths = $Paths; action = $Action; reason = "Fixture review of $Id"; validation = @('tools/check.ps1'); dependencies = @(); reReviewWhen = 'When desktop behavior is exercised'; upstreamEquivalent = 'Fixture source comparison'; localPaths = @() }
}
function Complete-Result {
    foreach ($batch in $decisions.batches) {
        $batch.result.outcome = $batch.action
        $batch.result.deviations = ''
        $batch.result.validation = @('Fixture hashes and actual applied source inspected; no product semantic claim')
    }
    Save-Decisions
}
try {
    foreach ($repo in @($upstream, $local)) {
        $null = New-Item -ItemType Directory -Force $repo
        $null = Invoke-FixtureGit $repo @('init', '--quiet')
        $null = Invoke-FixtureGit $repo @('config', 'user.name', 'Fixture')
        $null = Invoke-FixtureGit $repo @('config', 'user.email', 'fixture@example.invalid')
        $null = Invoke-FixtureGit $repo @('config', 'core.autocrlf', 'false')
    }
    foreach ($name in @('shared', 'retired', 'renamed', 'retained-name', 'watch', 'keep', 'mirror', 'deferred-mirror', 'normalized')) {
        Write-FixtureFile $upstream "src/$name.ts" "export const value = '$name';`n"
    }
    Write-FixtureFile $upstream 'README.md' "base`n"
    $null = Invoke-FixtureGit $upstream @('add', '.')
    $null = Invoke-FixtureGit $upstream @('commit', '--quiet', '-m', 'reviewed U0')
    $base = [string](Invoke-FixtureGit $upstream @('rev-parse', 'HEAD'))
    $null = New-Item -ItemType Directory -Force (Join-Path $local 'dashboard-src')
    Copy-Item -LiteralPath (Join-Path $upstream 'src') -Destination (Join-Path $local 'dashboard-src/src') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $upstream 'README.md') -Destination (Join-Path $local 'dashboard-src/README.md')
    Write-FixtureFile $local '.gitignore' ".tmp/`n"
    Write-FixtureFile $local 'tools/check.ps1' "Write-Host 'fixture check'`n"
    Write-FixtureFile $local 'docs/maintenance.md' '<a id="shared-behavior"></a><a id="dependent-behavior"></a>'
    Write-FixtureFile $local 'docs/source.json' 'SOURCE MUST SURVIVE'
    Write-FixtureFile $local 'dashboard-src/src/owned.ts' "export const desktop = true;`n"
    $fixturePolicy = [ordered]@{
        schemaVersion = 2
        upstream = @{ repository = 'https://example.invalid/upstream.git'; tag = 'fixture-U0'; revision = $base; localPrefix = 'dashboard-src/' }
        historicalReference = @{ tag = 'unavailable-tag'; revision = ('1' * 40) }
        forkOwnedPaths = @('docs/*', 'tools/*', '.gitignore', 'dashboard-src/src/owned.ts', 'dashboard-src/src/__tests__/*')
        mirrors = @(
            @{ path = 'dashboard-src/src/mirror.ts'; sha256 = Get-NormalizedSourceHash (Join-Path $upstream 'src/mirror.ts') },
            @{ path = 'dashboard-src/src/deferred-mirror.ts'; sha256 = Get-NormalizedSourceHash (Join-Path $upstream 'src/deferred-mirror.ts') }
        )
        contracts = @(
            @{ id = 'shared-behavior'; rule = 'docs/maintenance.md#shared-behavior'; paths = @('dashboard-src/src/*'); watch = @(); checks = @('tools/check.ps1'); risk = 'low' },
            @{ id = 'dependent-behavior'; rule = 'docs/maintenance.md#dependent-behavior'; paths = @('dashboard-src/src/shared.ts'); watch = @('dashboard-src/src/watch.ts'); checks = @('tools/check.ps1'); risk = 'high' }
        )
        pending = @(@{ id = 'pending-ui'; contract = 'shared-behavior'; question = 'Check appearance on actual desktop' })
        knownGaps = @(@{ id = 'manual-gap'; contract = 'shared-behavior'; expected = 'Real desktop'; observed = 'Fixture only'; coverage = 'Manual review remains' })
    }
    Save-FixturePolicy
    $null = Invoke-FixtureGit $local @('add', '.')
    $null = Invoke-FixtureGit $local @('commit', '--quiet', '-m', 'local baseline')
    Write-FixtureFile $local 'dashboard-src/src/shared.ts' "export const desktopShared = true;`n"
    Write-FixtureFile $local 'dashboard-src/src/normalized.ts' "export const value = 'normalized';`r`n"
    Write-FixtureFile $local 'tools/untracked.ps1' "owned untracked tool`n"
    foreach ($name in @('shared', 'watch', 'keep', 'mirror', 'deferred-mirror')) { Write-FixtureFile $upstream "src/$name.ts" "export const updated = '$name';`n" }
    Remove-Item -LiteralPath (Join-Path $upstream 'src/retired.ts')
    $null = Invoke-FixtureGit $upstream @('mv', 'src/renamed.ts', 'src/moved.ts')
    $null = Invoke-FixtureGit $upstream @('mv', 'src/retained-name.ts', 'src/retained-moved.ts')
    Write-FixtureFile $upstream 'src/new.ts' "export const added = true;`n"
    Write-FixtureFile $upstream 'src/snake_name.ts' "export const sourceName = 'upstream';`n"
    $null = Invoke-FixtureGit $upstream @('add', '.')
    $null = Invoke-FixtureGit $upstream @('commit', '--quiet', '-m', 'target U1: feature and mirrors')
    $target = [string](Invoke-FixtureGit $upstream @('rev-parse', 'HEAD'))
    $null = Invoke-FixtureGit $upstream @('checkout', '--quiet', $base)

    # Output and source trust boundaries apply before any report writes.
    Assert-Fails { & $entry -RepositoryRoot $local -UpstreamRoot $upstream -OutputDirectory $upstream } '*outside the read-only upstream checkout*'
    Assert-Fails { & $entry -RepositoryRoot $local -UpstreamRoot $upstream -OutputDirectory (Join-Path $local 'docs/reports') } '*ignored output paths*'
    $trackedSource = [IO.File]::ReadAllText((Join-Path $local 'docs/source.json'))
    Assert-Fails { & $entry -RepositoryRoot $local -UpstreamRoot $upstream -OutputDirectory $output -DecisionFile (Join-Path $local 'docs/source.json') } '*ignored output paths*'
    Assert-True ([IO.File]::ReadAllText((Join-Path $local 'docs/source.json')) -ceq $trackedSource) 'Output replaced source.'
    $null = New-Item -ItemType Directory -Force (Join-Path $local '.tmp')
    $link = Join-Path $local '.tmp/report-link'
    $linkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
    $null = New-Item -ItemType $linkType -Path $link -Target (Join-Path $local 'docs')
    Assert-Fails { & $entry -RepositoryRoot $local -UpstreamRoot $upstream -OutputDirectory $link } '*reparse points or symlink ancestors*'
    Remove-Item -LiteralPath $link -Force
    Write-FixtureFile $upstream 'dirty.txt' 'dirty'
    Assert-Fails { Invoke-Review } '*worktree must be clean*'
    Remove-Item -LiteralPath (Join-Path $upstream 'dirty.txt')
    $grafts = Join-Path $upstream '.git/info/grafts'
    Write-FixtureFile $upstream '.git/info/grafts' $base
    Assert-Fails { Invoke-Review } '*graft metadata*'
    Remove-Item -LiteralPath $grafts
    $fixturePolicy.contracts[0].rule = 'docs/maintenance.md#missing'
    Save-FixturePolicy
    Assert-Fails { Read-ForkPolicy -Root $local } '*Contract rule must reference*'
    $fixturePolicy.contracts[0].rule = 'docs/maintenance.md#shared-behavior'
    Write-FixtureFile $local 'docs/maintenance.md' '<a id="dependent-behavior"></a>'
    Save-FixturePolicy
    Assert-Fails { Read-ForkPolicy -Root $local } '*Missing explicit maintenance rule anchor*'
    Write-FixtureFile $local 'docs/maintenance.md' '<a id="shared-behavior"></a><a id="dependent-behavior"></a>'
    foreach ($pattern in @('dashboard-src/src/own*.ts', 'Dashboard-src/src/owned.ts')) {
        $fixturePolicy.forkOwnedPaths += $pattern
        Save-FixturePolicy
        $expected = if ($pattern.Contains('*')) { '*literal directory namespaces*' } else { '*canonical dashboard-src/ prefix*' }
        Assert-Fails { Read-ForkPolicy -Root $local } $expected
        $fixturePolicy.forkOwnedPaths = @($fixturePolicy.forkOwnedPaths | Where-Object { $_ -cne $pattern })
    }
    Save-FixturePolicy

    # Intra-run mutations are rejected, not silently incorporated into a source-bound plan.
    $mutation = @{ pending = $true }
    function Get-FileHash {
        param([string]$LiteralPath, [string]$Algorithm)
        $hash = Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm
        if ($mutation.pending -and $LiteralPath.EndsWith('untracked.ps1')) {
            $mutation.pending = $false
            [IO.File]::AppendAllText($LiteralPath, 'changed during snapshot')
        }
        return $hash
    }
    Assert-Fails { Invoke-Review } '*source inputs changed*'
    Remove-Item Function:Get-FileHash
    Invoke-Review
    $plan = Read-Record 'plan.json'
    $inventory = @(Read-Record 'inventory.json')
    Assert-True ($plan.candidateDirty -and @($inventory | Where-Object path -eq 'tools/untracked.ps1')[0].ownership -eq 'fork-owned') 'Dirty/untracked input omitted.'
    Assert-True (@($inventory | Where-Object path -eq 'dashboard-src/src/normalized.ts')[0].ownership -eq 'upstream') 'CRLF comparison drifted.'
    Assert-True (@($plan.incoming | Where-Object path -eq 'dashboard-src/src/shared.ts')[0].risk -eq 'high') 'Maximum contract risk lost.'
    Assert-True (@($plan.incoming | Where-Object path -eq 'dashboard-src/src/watch.ts')[0].attention -eq 'upstream-change') 'Incoming contract classification lost.'
    Assert-True ($plan.checks.Count -eq 1 -and $plan.affectedContracts.Count -eq 2) 'Checks must be deduplicated and affected contracts included.'
    Assert-True (@($plan.renameHints | Where-Object { $_ -like 'R*renamed*moved*' }).Count -eq 1 -and $plan.commitHints.Count -eq 1) 'Git commit and rename hints missing.'
    $planBytes = [IO.File]::ReadAllBytes((Join-Path $output 'plan.json'))
    $decisions = Read-Record 'decisions.json'
    Assert-True ($decisions.batches.Count -eq 0) 'Plan automatically approved unknown changes.'
    Assert-Fails { Invoke-Review 'Review' } '*requires batch coverage*'
    $decisions.batches = @(
        (New-Batch 'plain-adoption' @('dashboard-src/src/new.ts', 'dashboard-src/src/mirror.ts', 'dashboard-src/src/renamed.ts', 'dashboard-src/src/moved.ts') 'adopt'),
        (New-Batch 'source-adaptation' @('dashboard-src/src/shared.ts', 'dashboard-src/src/snake_name.ts') 'adapt'),
        (New-Batch 'consumer-behavior' @('dashboard-src/src/shared.ts', 'dashboard-src/src/watch.ts') 'adapt'),
        (New-Batch 'retain-product' @('dashboard-src/src/keep.ts', 'dashboard-src/src/retired.ts') 'keep-local'),
        (New-Batch 'deferred-mirror' @('dashboard-src/src/deferred-mirror.ts') 'defer'),
        (New-Batch 'deferred-renaming' @('dashboard-src/src/retained-name.ts', 'dashboard-src/src/retained-moved.ts', 'dashboard-src/src/deferred-mirror.ts') 'defer')
    )
    $decisions.batches[1].localPaths = @('dashboard-src/src/snakeName.ts', 'docs/upstream/ownership.json', 'tools/untracked.ps1')
    Save-Decisions
    Assert-Fails { Invoke-Review 'Review' } '*overlap requires an explicit exception*'
    $decisions.overlapExceptions = @([pscustomobject]@{ path = 'dashboard-src/src/shared.ts'; batches = @('source-adaptation', 'consumer-behavior'); action = 'adapt'; reason = 'One file contains both source and consumer behavior; both reviewed' })
    Save-Decisions
    Remove-Item -LiteralPath (Join-Path $output 'report.md')
    $null = New-Item -ItemType HardLink -Path (Join-Path $output 'report.md') -Target (Join-Path $local 'docs/source.json')
    Invoke-Review 'Review'
    Assert-True ([IO.File]::ReadAllText((Join-Path $local 'docs/source.json')) -ceq $trackedSource) 'Report hard link truncated a source file.'
    $report = [IO.File]::ReadAllText((Join-Path $output 'report.md'))
    Assert-True ($report.IndexOf('source-adaptation') -lt $report.IndexOf('Suggested') -or $report.Contains('Checks associated with implemented batches')) 'Functional summary or implementation checks missing.'
    Assert-True ($report.Contains('<details>') -and -not $report.Contains('## Paths needing decisions')) 'Large path/check hints were not collapsed.'
    Invoke-Review
    Assert-True ([Convert]::ToHexString($planBytes) -ceq [Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $output 'plan.json')))) 'Plan evidence was overwritten.'
    $decisions.batches[0].reason += ' changed after review'
    Save-Decisions
    Assert-Fails { Invoke-Review 'Result' } '*Decisions differ from frozen*'
    $decisions.batches[0].reason = 'Fixture review of plain-adoption'
    Save-Decisions

    # Actual integration: exact adoption from U1; adaptation explicitly names the local camelCase file.
    foreach ($path in @('new.ts', 'mirror.ts', 'moved.ts')) {
        $text = [string](Invoke-FixtureGit $upstream @('show', ($target + ':src/' + $path))) + "`n"
        Write-FixtureFile $local "dashboard-src/src/$path" $text
    }
    Remove-Item -LiteralPath (Join-Path $local 'dashboard-src/src/renamed.ts')
    Write-FixtureFile $local 'dashboard-src/src/shared.ts' "export const desktopShared = 'adapted';`n"
    Write-FixtureFile $local 'dashboard-src/src/watch.ts' "export const consumer = 'adapted';`n"
    Write-FixtureFile $local 'dashboard-src/src/snakeName.ts' "export const sourceName = 'adapted locally';`n"
    $fixturePolicy.adaptations = @(@{ path = 'dashboard-src/src/snakeName.ts'; upstreamPath = 'dashboard-src/src/snake_name.ts'; revision = $base })
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Result' } '*Adaptation upstream source does not exist*'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $output 'results.json'))) 'Invalid provenance was recorded as a completed result.'
    $fixturePolicy.adaptations[0].revision = $target
    Save-FixturePolicy
    Invoke-Review 'Result'
    $decisions = Read-Record 'decisions.json'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $output 'results.json'))) 'Empty actual validation was accepted.'
    Assert-Fails { Invoke-Review 'Result' } '*Result outcome must match*'
    Complete-Result
    Write-FixtureFile $local 'tools/untracked.ps1' 'changed after captured validation'
    Assert-Fails { Invoke-Review 'Result' } '*Result source changed*'
    Invoke-Review 'Result' -ResetResults
    $decisions = Read-Record 'decisions.json'
    Assert-True ($decisions.batches[0].result.validation.Count -eq 0) 'Reset reused obsolete results.'
    Complete-Result
    Write-FixtureFile $local 'dashboard-src/src/new.ts' 'not target bytes'
    Invoke-Review 'Result' -ResetResults
    $decisions = Read-Record 'decisions.json'
    Complete-Result
    Assert-Fails { Invoke-Review 'Result' } '*Adopt must match exact target source*'
    Write-FixtureFile $local 'dashboard-src/src/new.ts' "export const added = true;`n"
    Write-FixtureFile $local 'dashboard-src/src/unapproved.ts' 'undeclared addition'
    Invoke-Review 'Result' -ResetResults
    $decisions = Read-Record 'decisions.json'
    Complete-Result
    Assert-Fails { Invoke-Review 'Result' } '*Actual source change is outside declared batch*'
    Remove-Item -LiteralPath (Join-Path $local 'dashboard-src/src/unapproved.ts')
    Write-FixtureFile $local 'dashboard-src/src/deferred-mirror.ts' 'changed despite deferred'
    Invoke-Review 'Result' -ResetResults
    $decisions = Read-Record 'decisions.json'
    Complete-Result
    Assert-Fails { Invoke-Review 'Result' } '*Retained/deferred path changed*'
    Write-FixtureFile $local 'dashboard-src/src/deferred-mirror.ts' "export const value = 'deferred-mirror';`n"
    Invoke-Review 'Result' -ResetResults
    $decisions = Read-Record 'decisions.json'
    Complete-Result
    Invoke-Review 'Result'
    $results = Read-Record 'results.json'
    Assert-True ($results.changedPaths.path -contains 'dashboard-src/src/new.ts' -and $results.changedPaths.path -contains 'dashboard-src/src/snakeName.ts') 'Actual source change manifest missing.'
    Assert-True ($results.policyProposal.upstream.revision -ceq $target -and $results.policyProposal.pending.id -contains 'deferred-mirror') 'Explicit baseline proposal omitted source/defer.'
    $resultText = [IO.File]::ReadAllText((Join-Path $output 'results.json'))
    Write-FixtureFile $local '.tmp/reports/review/results.json' ($resultText.Replace('Fixture review of plain-adoption', 'tampered result'))
    Assert-Fails { Invoke-Review 'Result' } '*Immutable result evidence was modified*'
    Write-FixtureFile $local '.tmp/reports/review/results.json' $resultText
    Assert-True ($results.deferred.Count -eq 2 -and $results.candidateSnapshot.hash -cne $plan.candidateSnapshot.hash) 'Postimplementation results lost source binding/defer.'
    Invoke-Review 'Result'
    [IO.File]::AppendAllText((Join-Path $local 'dashboard-src/src/shared.ts'), 'changed after result')
    Assert-Fails { Invoke-Review 'Result' } '*Result source or decisions changed*'
    Write-FixtureFile $local 'dashboard-src/src/shared.ts' "export const desktopShared = 'adapted';`n"

    # Baseline closure cannot drop durable deferred work or relabel old mirrors as U1.
    $fixturePolicy.upstream.revision = $target
    $fixturePolicy.upstream.tag = 'fixture-U1'
    $fixturePolicy.mirrors[0].sha256 = Get-ForkUpstreamHash $upstream $target 'src/mirror.ts'
    Save-FixturePolicy
    $null = Invoke-FixtureGit $upstream @('checkout', '--quiet', $target)
    Assert-Fails { Invoke-Review 'Close' } '*Retained source mappings must match*'
    $fixturePolicy.adaptations = $results.policyProposal.adaptations
    Assert-True ($fixturePolicy.adaptations.path -contains 'dashboard-src/src/retired.ts') 'Retained upstream deletion lost durable provenance.'
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Close' } '*Deferred batch must be preserved*'
    $fixturePolicy.pending += $results.deferred
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Close' } '*Mirror baseline is not the reviewed upstream source*'
    $fixturePolicy.mirrors[1].revision = $base
    Save-FixturePolicy
    Invoke-Review 'Close'
    Invoke-Review 'Close'
    Assert-True ((Read-Record 'closure.json').comparisonBaseline -ceq $target) 'Baseline closure not recorded.'
    $reviewedMirrors = $fixturePolicy.mirrors
    $fixturePolicy.mirrors = @()
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Close' } '*Reviewed mirror cannot disappear*'
    $fixturePolicy.mirrors = $reviewedMirrors
    $fixturePolicy.pending = @($fixturePolicy.pending | Where-Object id -ne 'deferred-mirror')
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Close' } '*Deferred batch must be preserved*'
    $fixturePolicy.pending = @($fixturePolicy.pending | Where-Object { $null -eq $_.paths }) + @($results.deferred)
    Save-FixturePolicy
    Write-FixtureFile $local 'tools/untracked.ps1' 'source changed after final result'
    Assert-Fails { Invoke-Review 'Close' } '*Result source changed after validation*'
    Write-FixtureFile $local 'tools/untracked.ps1' 'changed after captured validation'
    Invoke-Review 'Close'

    # The next round retains the deferred path even when the upstream diff is empty.
    $output = Join-Path $local '.tmp/reports/next-round'
    Invoke-Review
    $next = Read-Record 'plan.json'
    Assert-True ($next.incoming.Count -eq 0 -and $next.requiredPaths -contains 'dashboard-src/src/deferred-mirror.ts' -and $next.requiredPaths -contains 'dashboard-src/src/retained-name.ts') 'Deferred work disappeared after baseline advancement.'
    Assert-True ($next.pendingDecisions.id -contains 'deferred-mirror') 'Durable defer was omitted.'
    Assert-True ($next.unclassified.Count -eq 0) 'Declared adaptation or retained mirror was unclassified in the next round.'
    $decisions = Read-Record 'decisions.json'
    $decisions.batches = @(
        (New-Batch 'deferred-mirror' @('dashboard-src/src/deferred-mirror.ts') 'defer'),
        (New-Batch 'deferred-renaming' @('dashboard-src/src/retained-name.ts', 'dashboard-src/src/retained-moved.ts', 'dashboard-src/src/deferred-mirror.ts') 'defer')
    )
    Save-Decisions
    Invoke-Review 'Review'
    $planText = [IO.File]::ReadAllText((Join-Path $output 'plan.json'))
    Write-FixtureFile $local '.tmp/reports/next-round/plan.json' ($planText.Replace('fixture-U1', 'tampered-U1'))
    Assert-Fails { Invoke-Review 'Review' } '*Immutable plan evidence was modified*'
    Write-FixtureFile $local '.tmp/reports/next-round/plan.json' $planText
    Write-FixtureFile $local '.tmp/reports/next-round/inventory.json' '[]'
    Assert-Fails { Invoke-Review 'Review' } '*Immutable plan evidence was modified*'
    Write-Host 'Functional batches, actual target adoption/adaptation, immutable evidence, result source binding and durable baseline/defer fixtures passed.'
}
finally {
    if (Test-Path Function:Get-FileHash) { Remove-Item Function:Get-FileHash }
    Get-ChildItem -LiteralPath $fixture -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
    Remove-TemporaryTestDirectory $fixture
}
