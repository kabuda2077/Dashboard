#requires -Version 7.0
# Offline fixtures exercise actual edits, candidate registration and source-bound completion.
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
function Assert-True { param([bool]$Value, [string]$Message); if (-not $Value) { throw $Message } }
function Assert-Fails {
    param([scriptblock]$Action, [string]$MessagePattern)
    $caught = $false
    try { & $Action | Out-Null }
    catch { if ($_.Exception.Message -notlike $MessagePattern) { throw "Unexpected error (expected $MessagePattern): $($_.Exception.Message)" }; $caught = $true }
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
function Save-FixturePolicy { Write-FixtureFile $local 'docs/upstream/ownership.json' (ConvertTo-Json -InputObject $fixturePolicy -Depth 60) }
function Invoke-Review {
    param([string]$Phase = 'Plan', [switch]$AdvanceBaseline, [string[]]$Revisit = @())
    & $entry -RepositoryRoot $local -UpstreamRoot $upstream -TargetRef $target -OutputDirectory $output -Phase $Phase -AdvanceBaseline:$AdvanceBaseline -Revisit $Revisit
}
function Read-Record { param([string]$Name); return [IO.File]::ReadAllText((Join-Path $output $Name)) | ConvertFrom-Json }
function Save-Decisions { Write-FixtureFile $local ([IO.Path]::GetRelativePath($local, (Join-Path $output 'decisions.json'))) (ConvertTo-Json -InputObject $decisions -Depth 60) }
function New-Batch {
    param([string]$Id, [string[]]$Paths, [string]$Action)
    return [pscustomobject]@{ id = $Id; paths = $Paths; localPaths = @(); action = $Action; reason = "Fixture review of $Id"; dependencies = @(); reReviewWhen = 'When desktop behavior is exercised' }
}
function Add-Evidence {
    $script:decisions = Read-Record 'decisions.json'
    $decisions.review = 'Actual applied fixture source and provenance inspected; no desktop acceptance claimed.'
    $decisions.evidence = @('Offline fixture assertions and source hashes checked against the registered candidate')
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
    foreach ($name in @('shared', 'retired', 'renamed', 'retained-name', 'watch', 'keep', 'mirror', 'deferred-mirror', 'normalized')) { Write-FixtureFile $upstream "src/$name.ts" "export const value = '$name';`n" }
    Write-FixtureFile $upstream 'README.md' "base`n"
    Write-FixtureFile $upstream 'public/favicon.ico' 'favicon source'
    $null = Invoke-FixtureGit $upstream @('add', '.')
    $null = Invoke-FixtureGit $upstream @('commit', '--quiet', '-m', 'reviewed U0')
    $base = [string](Invoke-FixtureGit $upstream @('rev-parse', 'HEAD'))
    $null = New-Item -ItemType Directory -Force (Join-Path $local 'dashboard-src')
    Copy-Item -LiteralPath (Join-Path $upstream 'src') -Destination (Join-Path $local 'dashboard-src/src') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $upstream 'public') -Destination (Join-Path $local 'dashboard-src/public') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $upstream 'README.md') -Destination (Join-Path $local 'dashboard-src/README.md')
    Write-FixtureFile $local 'resources/app.ico' 'generated app icon'
    Write-FixtureFile $local 'resources/tray.ico' 'generated tray icon'
    Write-FixtureFile $local '.gitignore' ".tmp/`n"
    Write-FixtureFile $local 'tools/check.ps1' "Write-Host 'fixture check'`n"
    Write-FixtureFile $local 'docs/development.md' '<a id="shared-behavior"></a>'
    Write-FixtureFile $local 'docs/upstream.md' '<a id="dependent-behavior"></a>'
    Write-FixtureFile $local 'docs/source.json' 'SOURCE MUST SURVIVE'
    Write-FixtureFile $local 'dashboard-src/src/owned.ts' "export const desktop = true;`n"
    $largePendingPaths = @('dashboard-src/src/shared.ts', 'dashboard-src/src/normalized.ts') + @(1..302 | ForEach-Object { "dashboard-src/src/old-pending-$_.ts" })
    $fixturePolicy = [ordered]@{
        schemaVersion = 2
        upstream = @{ repository = 'https://example.invalid/upstream.git'; tag = 'fixture-U0'; revision = $base; localPrefix = 'dashboard-src/' }
        historicalReference = @{ tag = 'unavailable-tag'; revision = ('1' * 40) }
        forkOwnedPaths = @('docs/*', 'tools/*', 'resources/*', '.gitignore', 'dashboard-src/src/owned.ts', 'dashboard-src/src/__tests__/*')
        mirrors = @(
            @{ path = 'dashboard-src/src/mirror.ts'; sha256 = Get-NormalizedSourceHash (Join-Path $upstream 'src/mirror.ts') },
            @{ path = 'dashboard-src/src/deferred-mirror.ts'; sha256 = Get-NormalizedSourceHash (Join-Path $upstream 'src/deferred-mirror.ts') }
        )
        contracts = @(
            @{ id = 'shared-behavior'; rule = 'docs/development.md#shared-behavior'; paths = @('dashboard-src/src/*'); watch = @(); checks = @('tools/check.ps1'); risk = 'low' },
            @{ id = 'dependent-behavior'; rule = 'docs/upstream.md#dependent-behavior'; paths = @('dashboard-src/src/shared.ts'); watch = @('dashboard-src/src/watch.ts'); checks = @('tools/check.ps1'); risk = 'high' }
        )
        pending = @(
            @{ id = 'pending-ui'; contract = 'shared-behavior'; question = 'Check appearance on actual desktop' },
            @{ id = 'old-large-group'; paths = $largePendingPaths; reason = 'Original reason must survive'; reReviewWhen = 'Original reconsideration condition'; dependencies = @() }
        )
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
    Write-FixtureFile $upstream 'feature/new.ts' "export const contractRequired = true;`n"
    $null = Invoke-FixtureGit $upstream @('add', '.')
    $null = Invoke-FixtureGit $upstream @('commit', '--quiet', '-m', 'target U1: feature and mirrors')
    $target = [string](Invoke-FixtureGit $upstream @('rev-parse', 'HEAD'))
    $realTarget = $target
    $null = Invoke-FixtureGit $upstream @('checkout', '--quiet', $base)

    # Trust boundaries remain enforced before report or source writes.
    Assert-Fails { & $entry -RepositoryRoot $local -UpstreamRoot $upstream -OutputDirectory $upstream } '*outside the read-only upstream checkout*'
    Assert-Fails { & $entry -RepositoryRoot $local -UpstreamRoot $upstream -OutputDirectory (Join-Path $local 'docs/reports') } '*ignored output paths*'
    Assert-Fails { & $entry -RepositoryRoot $local -UpstreamRoot $upstream -OutputDirectory $output -DecisionFile (Join-Path $local 'docs/source.json') } '*ignored output paths*'
    $null = New-Item -ItemType Directory -Force (Join-Path $local '.tmp')
    $link = Join-Path $local '.tmp/report-link'
    $null = New-Item -ItemType $(if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }) -Path $link -Target (Join-Path $local 'docs')
    Assert-Fails { & $entry -RepositoryRoot $local -UpstreamRoot $upstream -OutputDirectory $link } '*reparse points or symlink ancestors*'
    Remove-Item -LiteralPath $link -Force
    Write-FixtureFile $upstream 'dirty.txt' 'dirty'
    Assert-Fails { Invoke-Review } '*worktree must be clean*'
    Remove-Item -LiteralPath (Join-Path $upstream 'dirty.txt')
    foreach ($repo in @($upstream, $local)) {
        Write-FixtureFile $repo '.git/info/grafts' $base
        Assert-Fails { Invoke-Review } '*graft metadata*'
        Remove-Item -LiteralPath (Join-Path $repo '.git/info/grafts')
    }
    foreach ($rule in @('docs/development.md#missing', '../development.md#shared-behavior', 'docs/other.md#shared-behavior')) {
        $fixturePolicy.contracts[0].rule = $rule
        Save-FixturePolicy
        Assert-Fails { Read-ForkPolicy -Root $local } '*Contract rule must reference*'
    }
    $fixturePolicy.contracts[0].rule = 'docs/development.md#shared-behavior'
    Write-FixtureFile $local 'docs/development.md' ''
    Save-FixturePolicy
    Assert-Fails { Read-ForkPolicy -Root $local } '*Missing explicit rule anchor*'
    Remove-Item -LiteralPath (Join-Path $local 'docs/development.md')
    Assert-Fails { Read-ForkPolicy -Root $local } '*Missing explicit rule anchor*'
    Write-FixtureFile $local 'docs/development.md' '<a id="shared-behavior"></a>'
    foreach ($pattern in @('dashboard-src/src/own*.ts', 'Dashboard-src/src/owned.ts')) {
        $fixturePolicy.forkOwnedPaths += $pattern
        Save-FixturePolicy
        Assert-Fails { Read-ForkPolicy -Root $local } $(if ($pattern.Contains('*')) { '*literal directory namespaces*' } else { '*canonical dashboard-src/ prefix*' })
        $fixturePolicy.forkOwnedPaths = @($fixturePolicy.forkOwnedPaths | Where-Object { $_ -cne $pattern })
    }
    Save-FixturePolicy

    # Zero incoming silently carries old pending, without checks or forced batch rituals.
    $target = $base
    $output = Join-Path $local '.tmp/reports/zero'
    Invoke-Review
    $zeroPlan = Read-Record 'plan.json'
    Assert-True ($zeroPlan.requiredPaths.Count -eq 0 -and $zeroPlan.checks.Count -eq 0 -and $zeroPlan.triggeredPending.Count -eq 0) 'Old pending created required paths/checks without incoming.'
    Assert-True (-not [IO.File]::ReadAllText((Join-Path $output 'report.md')).Contains('Original reason must survive')) 'Untriggered pending expanded in report.'
    Write-FixtureFile $local 'resources/app.ico' 'app icon rebuilt after Plan'
    Invoke-Review 'Prepare'
    $zeroPolicy = Read-ForkPolicy -Root $local
    Assert-True (($zeroPolicy.pending | ConvertTo-Json -Depth 30 -Compress) -ceq ($fixturePolicy.pending | ConvertTo-Json -Depth 30 -Compress)) 'Untouched pending did not carry verbatim.'
    Assert-Fails { Invoke-Review 'Complete' } '*review actual*'
    Add-Evidence
    Write-FixtureFile $local 'resources/tray.ico' 'tray icon rebuilt after Prepare'
    Write-FixtureFile $local 'dashboard-src/public/favicon.ico' 'changed favicon source'
    Assert-Fails { Invoke-Review 'Complete' } '*Prepared source or decisions changed*'
    Write-FixtureFile $local 'dashboard-src/public/favicon.ico' 'favicon source'
    Invoke-Review 'Complete'
    # Explicit reconsideration selects only the requested pending ID.
    $output = Join-Path $local '.tmp/reports/revisit'
    Invoke-Review -Revisit @('old-large-group')
    Assert-True ((Read-Record 'plan.json').requiredPaths.Count -eq 304) 'Explicit Revisit did not select full requested scope.'
    Save-FixturePolicy
    # A manually revisited question has a decision even when it has no file paths.
    $output = Join-Path $local '.tmp/reports/question'
    Invoke-Review -Revisit @('pending-ui')
    Assert-True ((Read-Record 'plan.json').requiredPaths.Count -eq 0) 'Question reconsideration invented file paths.'
    Assert-Fails { Invoke-Review 'Prepare' } '*Triggered question requires an explicit same-ID decision*'
    $decisions = Read-Record 'decisions.json'
    $decisions.batches = @((New-Batch 'unrelated-empty-group' @() 'keep-local'))
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*batch.paths must be an array with at least one entry*'
    $decisions.batches = @((New-Batch 'pending-ui' @() 'pending'))
    $decisions.batches[0].PSObject.Properties.Remove('localPaths')
    Save-Decisions
    Invoke-Review 'Review'
    Assert-True ((Read-Record 'decisions.json').history[-1].batches[0].localPaths -eq 0) 'Omitted localPaths invented consumer scope for a pathless question.'
    Assert-Fails { Invoke-Review 'Prepare' } '*Unresolved user decisions*'
    $decisions = Read-Record 'decisions.json'
    $decisions.batches[0].action = 'defer'
    $decisions.revisionReason = 'Keep the question open until actual desktop review'
    Save-Decisions
    Invoke-Review 'Prepare'
    $question = @((Read-ForkPolicy -Root $local).pending | Where-Object id -eq 'pending-ui')[0]
    Assert-True ($null -eq $question.paths -and $question.question -ceq 'Check appearance on actual desktop' -and $question.reason -ceq 'Fixture review of pending-ui') 'Pathless defer lost its original question or explicit rationale.'
    Add-Evidence
    Invoke-Review 'Prepare'
    Assert-True ((Read-Record 'decisions.json').evidence.Count -eq 1) 'Identical question Prepare discarded evidence.'
    $decisions = Read-Record 'decisions.json'
    $decisions.batches[0].action = 'keep-local'
    $decisions.batches[0].reason = 'Retain the existing desktop appearance; the product question is resolved'
    $decisions.revisionReason = 'Resolve the question with the local product choice'
    Save-Decisions
    Invoke-Review 'Prepare'
    Assert-True ('pending-ui' -cnotin @((Read-ForkPolicy -Root $local).pending.id)) 'Pathless keep-local could not resolve the question.'
    Assert-True ((Read-Record 'decisions.json').evidence.Count -eq 0) 'Resolved question reused validation for an earlier decision.'
    Save-FixturePolicy
    $output = Join-Path $local '.tmp/reports/question-adopt'
    Invoke-Review -Revisit @('pending-ui')
    $decisions = Read-Record 'decisions.json'
    $decisions.batches = @((New-Batch 'pending-ui' @() 'adopt'))
    Save-Decisions
    Invoke-Review 'Prepare'
    Assert-True ('pending-ui' -cnotin @((Read-ForkPolicy -Root $local).pending.id)) 'Pathless adopt could not resolve the question.'
    Save-FixturePolicy
    $target = $realTarget
    $output = Join-Path $local '.tmp/reports/review'

    $null = New-Item -ItemType Directory -Force $output
    $sourceBytes = [IO.File]::ReadAllBytes((Join-Path $local 'docs/source.json'))
    $null = New-Item -ItemType HardLink -Path (Join-Path $output 'report.md') -Target (Join-Path $local 'docs/source.json')
    $fixturePolicy.forkOwnedPaths += 'dashboard-src/feature/new.ts'
    Save-FixturePolicy
    $mutation = @{ pending = $true }
    function Get-FileHash {
        param([string]$LiteralPath, [string]$Algorithm)
        $hash = Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm
        if ($mutation.pending -and $LiteralPath.EndsWith('untracked.ps1')) { $mutation.pending = $false; [IO.File]::AppendAllText($LiteralPath, 'changed during snapshot') }
        return $hash
    }
    Assert-Fails { Invoke-Review } '*source inputs changed*'
    Remove-Item Function:Get-FileHash
    Invoke-Review
    $plan = Read-Record 'plan.json'
    $inventory = @(Read-Record 'inventory.json')
    Assert-True ([Convert]::ToBase64String($sourceBytes) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $local 'docs/source.json')))) 'Report hardlink overwrote source.'
    Assert-True (@($plan.targetDiagnostics | Where-Object kind -eq 'fork-owned-collision').path -contains 'dashboard-src/feature/new.ts') 'Target ownership collision was not diagnosed at Plan.'
    $fixturePolicy.forkOwnedPaths = @($fixturePolicy.forkOwnedPaths | Where-Object { $_ -cne 'dashboard-src/feature/new.ts' })
    Save-FixturePolicy
    Assert-True ($plan.candidateDirty -and @($inventory | Where-Object path -eq 'tools/untracked.ps1')[0].ownership -eq 'fork-owned') 'Dirty/untracked input omitted.'
    $normalizedRow = @($inventory | Where-Object path -eq 'dashboard-src/src/normalized.ts')[0]
    Assert-True ($normalizedRow.upstreamSha256 -ceq $normalizedRow.candidateSha256) 'CRLF comparison drifted.'
    Assert-True (@($plan.incoming | Where-Object path -eq 'dashboard-src/src/shared.ts')[0].risk -eq 'high') 'Maximum contract risk lost.'
    Assert-True ($plan.checks.Count -eq 1 -and $plan.affectedContracts.Count -eq 2) 'Checks were not deduplicated.'
    Assert-True (@($plan.triggeredPending | Where-Object id -eq 'old-large-group')[0].paths.Count -eq 1 -and 'dashboard-src/src/normalized.ts' -cnotin $plan.requiredPaths) 'Partial incoming forced all 304 old paths.'
    Assert-True (@($plan.targetDiagnostics | Where-Object kind -eq 'target-new-file-needs-contract').path -contains 'dashboard-src/feature/new.ts') 'Target new file contract was not diagnosed at Plan.'
    $planBytes = [IO.File]::ReadAllBytes((Join-Path $output 'plan.json'))
    $decisions = Read-Record 'decisions.json'
    Invoke-Review 'Review'
    Assert-Fails { Invoke-Review 'Prepare' } '*requires batch coverage*'
    $decisions = Read-Record 'decisions.json'
    $decisions.revisionReason = 'Group functional changes after source inspection'
    $decisions.batches = @(
        (New-Batch 'plain-adoption' @('dashboard-src/src/new.ts', 'dashboard-src/src/mirror.ts', 'dashboard-src/src/renamed.ts', 'dashboard-src/src/moved.ts', 'dashboard-src/feature/new.ts', 'dashboard-src/src/retired.ts') 'adopt'),
        (New-Batch 'source-adaptation' @('dashboard-src/src/shared.ts', 'dashboard-src/src/snake_name.ts') 'adapt'),
        (New-Batch 'consumer-behavior' @('dashboard-src/src/shared.ts', 'dashboard-src/src/watch.ts') 'adapt'),
        (New-Batch 'retained-file' @('dashboard-src/src/retained-name.ts') 'keep-local'),
        (New-Batch 'later-feature' @('dashboard-src/src/retained-moved.ts', 'dashboard-src/src/deferred-mirror.ts', 'dashboard-src/src/keep.ts') 'defer'),
        (New-Batch 'same-defer-overlap' @('dashboard-src/src/keep.ts') 'defer'),
        (New-Batch 'pending-ui' @() 'keep-local')
    )
    # Null optional consumer scope is equivalent to omission; no filler array required.
    $decisions.batches[0].localPaths = $null
    $functionalBatches = $decisions.batches
    $decisions.batches = @(
        (New-Batch 'early-scope' @($plan.requiredPaths) 'defer'),
        (New-Batch 'old-large-group' @('dashboard-src/src/shared.ts') 'defer'),
        (New-Batch 'pending-ui' @() 'defer')
    )
    Save-Decisions
    Invoke-Review 'Prepare'
    $provisional = Read-ForkPolicy -Root $local
    $oldGroup = @($provisional.pending | Where-Object id -eq 'old-large-group')[0]
    Assert-True ($oldGroup.paths.Count -eq 304 -and $oldGroup.reason -ceq 'Original reason must survive' -and $oldGroup.scopeUpdates[0].paths.Count -eq 1) 'Partial defer lost untouched scope or original rationale.'
    $decisions = Read-Record 'decisions.json'
    $decisions.batches = $functionalBatches
    $decisions.revisionReason = 'Implement selected changes after provisional scope review'
    Save-Decisions
    Invoke-Review 'Review'
    $adoptionSummary = @((Read-Record 'decisions.json').history[-1].batches | Where-Object id -eq 'plain-adoption')[0]
    Assert-True ($adoptionSummary.localPaths -eq 0) 'Null localPaths invented consumer scope.'
    Assert-True ([IO.File]::ReadAllText((Join-Path $output 'report.md')).Contains('plain-adoption** — adopt, 6 scope paths.')) 'Null localPaths added an empty report path.'
    # Implementation starts, then the scope and reasons can still be revised against the same Plan.
    Write-FixtureFile $local 'dashboard-src/src/shared.ts' "export const adaptedDesktop = true;`n"
    Write-FixtureFile $local 'dashboard-src/src/watch.ts' "export const adaptedConsumer = true;`n"
    $decisions = Read-Record 'decisions.json'
    $decisions.batches[1].localPaths = @('dashboard-src/src/sourceName.ts')
    $decisions.revisionReason = 'Retain local filename and desktop consumer behavior after implementation inspection'
    Save-Decisions
    Invoke-Review 'Review'
    Invoke-Review 'Plan'
    Assert-True ([Convert]::ToBase64String($planBytes) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $output 'plan.json')))) 'Scope revision replaced original Plan.'
    Assert-True ([IO.File]::ReadAllText((Join-Path $output 'report.md')).Contains('Retain local filename')) 'Revision reason was not reported.'
    # Conflicting handling requires an explicit exception, while same-action overlap needs none.
    $decisions = Read-Record 'decisions.json'
    $decisions.batches += New-Batch 'conflict' @('dashboard-src/src/shared.ts') 'keep-local'
    Save-Decisions
    Assert-Fails { Invoke-Review 'Review' } '*action conflict*'
    $decisions.overlapExceptions = @([pscustomobject]@{ path = 'dashboard-src/src/shared.ts'; batches = @('source-adaptation', 'consumer-behavior', 'conflict'); action = 'adapt'; reason = 'Explicitly adapt the overlapping consumer scope' })
    Save-Decisions
    Invoke-Review 'Review'
    $decisions = Read-Record 'decisions.json'
    $decisions.overlapExceptions = @()
    $decisions.revisionReason = 'Remove the redundant retained scope and resolve the actual consumer separately'
    $decisions.batches = @($decisions.batches | Where-Object id -ne 'conflict')
    $decisions.batches[2].action = 'pending'
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*action conflict*'
    $decisions.batches[2].paths = @('dashboard-src/src/watch.ts')
    Save-Decisions
    Invoke-Review 'Review'
    Assert-Fails { Invoke-Review 'Prepare' } '*Unresolved user decisions*'
    Assert-Fails { Invoke-Review 'Complete' } '*Unresolved user decisions*'
    $decisions = Read-Record 'decisions.json'
    $decisions.batches[2].action = 'adapt'
    $decisions.revisionReason = 'Resolve consumer scope to explicit adaptation'
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*Adopt must match exact target source*'
    foreach ($path in @('src/new.ts', 'src/mirror.ts', 'src/moved.ts', 'feature/new.ts')) {
        $content = [string](Invoke-FixtureGit $upstream @('show', ($target + ':' + $path)))
        Write-FixtureFile $local ('dashboard-src/' + $path) ($content + "`n")
    }
    Remove-Item -LiteralPath (Join-Path $local 'dashboard-src/src/renamed.ts'), (Join-Path $local 'dashboard-src/src/retired.ts')
    Write-FixtureFile $local 'dashboard-src/src/sourceName.ts' "export const sourceName = 'local adapter';`n"
    $fixturePolicy.adaptations = @(@{ path = 'dashboard-src/src/sourceName.ts'; upstreamPath = 'dashboard-src/src/no-such-source.ts'; revision = $target })
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Prepare' } '*Adaptation upstream source does not exist*'
    $fixturePolicy.adaptations[0].upstreamPath = 'dashboard-src/src'
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Prepare' } '*Adaptation upstream source does not exist*'
    $fixturePolicy.adaptations[0].upstreamPath = 'dashboard-src/src/snake_name.ts'
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Prepare' } '*requires a contract*'
    $fixturePolicy.contracts[0].paths += 'dashboard-src/feature/*'
    Save-FixturePolicy
    Write-FixtureFile $local 'dashboard-src/src/unclassified.ts' 'unexpected source'
    Assert-Fails { Invoke-Review 'Prepare' } '*Actual source change is outside*'
    Remove-Item -LiteralPath (Join-Path $local 'dashboard-src/src/unclassified.ts')
    # localPaths must participate in effective disposition, overriding a retained incoming name.
    $decisions.batches[1].localPaths += 'dashboard-src/src/keep.ts'
    $decisions.revisionReason = 'Declare the actual retained-name adaptation in localPaths'
    Save-Decisions
    Write-FixtureFile $local 'dashboard-src/src/keep.ts' "export const locallyAdapted = true;`n"
    # Independent functional scopes can share an edited file without implying each is complete.
    $decisions.batches += @(
        (New-Batch 'old-large-group' @('dashboard-src/src/shared.ts') 'defer'),
        (New-Batch 'new-structure-work' @('dashboard-src/src/shared.ts') 'defer')
    )
    $decisions.overlapExceptions = @([pscustomobject]@{
        path = 'dashboard-src/src/shared.ts'; batches = @('source-adaptation', 'old-large-group', 'new-structure-work'); action = 'adapt'
        reason = 'Adopt only the small fix in this shared file; both structure scopes remain deferred'
    })
    Save-Decisions
    # A missing mirror entry cannot bless an undeclared mirror edit.
    $fixturePolicy.mirrors = @($fixturePolicy.mirrors | Where-Object path -ne 'dashboard-src/src/deferred-mirror.ts')
    Save-FixturePolicy
    Write-FixtureFile $local 'dashboard-src/src/deferred-mirror.ts' 'undeclared mirror edit'
    Assert-Fails { Invoke-Review 'Prepare' } '*Mirror changed without declared disposition*'
    Write-FixtureFile $local 'dashboard-src/src/deferred-mirror.ts' "export const value = 'deferred-mirror';`n"
    Invoke-Review 'Prepare'
    $partial = Read-ForkPolicy -Root $local
    Assert-True ($partial.upstream.revision -ceq $base -and @($partial.adaptations | Where-Object path -eq 'dashboard-src/feature/new.ts')[0].revision -ceq $target) 'Partial adoption lost new-file provenance under U0.'
    Assert-True ('early-scope' -cnotin @($partial.pending.id)) 'Revised decisions retained obsolete provisional deferrals.'
    $mainOutput = $output
    $output = Join-Path $local '.tmp/reports/after-partial'
    Invoke-Review
    Assert-True (@((Read-Record 'inventory.json') | Where-Object path -eq 'dashboard-src/feature/new.ts')[0].ownership -eq 'shared') 'Next Plan lost partial target-file ownership.'
    $output = $mainOutput
    Invoke-Review 'Prepare' -AdvanceBaseline
    $registered = Read-ForkPolicy -Root $local
    Assert-True ($registered.upstream.revision -ceq $target -and [string](Invoke-FixtureGit $upstream @('rev-parse', 'HEAD')) -ceq $base) 'Prepare required target checkout or failed to advance candidate.'
    $retainedGroup = @($registered.pending | Where-Object id -eq 'old-large-group')[0]
    Assert-True ($retainedGroup.paths.Count -eq 304 -and 'dashboard-src/src/shared.ts' -cin $retainedGroup.paths) 'A small fix in the same file incorrectly closed old structure work.'
    Assert-True ($retainedGroup.reason -ceq 'Original reason must survive' -and $retainedGroup.scopeUpdates[0].reason -ceq 'Fixture review of old-large-group') 'Original or same-ID defer rationale was lost.'
    Assert-True (@($registered.pending | Where-Object id -eq 'new-structure-work')[0].paths -contains 'dashboard-src/src/shared.ts') 'Global adapted disposition discarded a new deferred functional scope.'
    Assert-True ([IO.File]::ReadAllText((Join-Path $output 'report.md')).Contains('**new-structure-work**')) 'Report hid a deferred scope sharing an adapted file.'
    Assert-True (@($registered.adaptations | Where-Object path -eq 'dashboard-src/src/retained-name.ts')[0].revision -ceq $base) 'Deleted upstream file lost retained source provenance.'
    Assert-True (@($registered.mirrors | Where-Object path -eq 'dashboard-src/src/deferred-mirror.ts')[0].revision -ceq $base) 'Deleted mirror registration bypassed provenance.'
    $prepared = Read-Record 'prepared.json'
    $policyHash = (Get-FileHash -LiteralPath (Join-Path $local 'docs/upstream/ownership.json')).Hash.ToLowerInvariant()
    Assert-True (@($prepared.candidateSnapshot.manifest | Where-Object path -eq 'docs/upstream/ownership.json')[0].sha256 -ceq $policyHash) 'Validation snapshot preceded final candidate registration.'
    Add-Evidence
    & $entry -RepositoryRoot $local -OutputDirectory $output -Phase Prepare
    Assert-True ((Read-Record 'decisions.json').evidence.Count -eq 1) 'Same-input Prepare discarded actual evidence or lost the original default checkout.'
    $decisions = Read-Record 'decisions.json'
    @($decisions.batches | Where-Object id -eq 'old-large-group')[0].action = 'adapt'
    $decisions.revisionReason = 'Explicitly complete only this one path of the old structure group'
    Save-Decisions
    Invoke-Review 'Prepare'
    $resolvedScope = Read-ForkPolicy -Root $local
    Assert-True (@($resolvedScope.pending | Where-Object id -eq 'old-large-group')[0].paths.Count -eq 303) 'Same-ID completion did not remove only its declared subset.'
    Assert-True (@($resolvedScope.pending | Where-Object id -eq 'new-structure-work')[0].paths -contains 'dashboard-src/src/shared.ts') 'Resolving one feature closed another feature on the same file.'
    Add-Evidence
    $decisions = Read-Record 'decisions.json'
    $decisions.batches[1].reason = 'Refined assessment after inspecting the exact registered candidate'
    $decisions.revisionReason = 'Clarify source adaptation rationale'
    Save-Decisions
    Assert-Fails { Invoke-Review 'Complete' } '*Prepared source or decisions changed*'
    Invoke-Review 'Prepare'
    Assert-True ((Read-Record 'decisions.json').evidence.Count -eq 0) 'Revised decisions reused old validation evidence.'
    Add-Evidence
    # Raw source changes invalidate evidence even when normalized content is unchanged.
    Write-FixtureFile $local 'dashboard-src/src/shared.ts' "export const adaptedDesktop = true;`r`n"
    Assert-Fails { Invoke-Review 'Complete' } '*Prepared source or decisions changed*'
    Invoke-Review 'Prepare'
    Assert-True ((Read-Record 'decisions.json').evidence.Count -eq 0) 'New candidate reused old validation evidence.'
    Add-Evidence
    $beforeComplete = (Get-FileHash -LiteralPath (Join-Path $local 'docs/upstream/ownership.json')).Hash
    Invoke-Review 'Complete'
    Assert-True ($beforeComplete -ceq (Get-FileHash -LiteralPath (Join-Path $local 'docs/upstream/ownership.json')).Hash) 'Complete changed candidate source.'
    $resultBytes = [IO.File]::ReadAllBytes((Join-Path $output 'results.json'))
    Invoke-Review 'Complete'
    Assert-True ([Convert]::ToBase64String($resultBytes) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $output 'results.json')))) 'Repeated completion rewrote immutable results.'
    Write-FixtureFile $local 'dashboard-src/src/shared.ts' 'changed after completion'
    Assert-Fails { Invoke-Review 'Prepare' } '*Completed round is immutable*'
    Assert-Fails { Invoke-Review 'Complete' } '*Completed round is immutable*'
    Write-FixtureFile $local 'dashboard-src/src/shared.ts' "export const adaptedDesktop = true;`r`n"
    $inventoryPath = Join-Path $output 'inventory.json'
    [IO.File]::AppendAllText($inventoryPath, ' ')
    Assert-Fails { Invoke-Review 'Review' } '*Immutable plan evidence was modified*'
    # A released target already contained in U0 supplements missing results without reversing newer work.
    $upstream = Join-Path $fixture 'contained-upstream'
    $local = Join-Path $fixture 'contained-candidate'
    $output = Join-Path $local '.tmp/reports/supplement'
    foreach ($repo in @($upstream, $local)) {
        $null = New-Item -ItemType Directory -Force $repo
        $null = Invoke-FixtureGit $repo @('init', '--quiet')
        $null = Invoke-FixtureGit $repo @('config', 'user.name', 'Fixture')
        $null = Invoke-FixtureGit $repo @('config', 'user.email', 'fixture@example.invalid')
        $null = Invoke-FixtureGit $repo @('config', 'core.autocrlf', 'false')
    }
    foreach ($name in @('adopt', 'adapt', 'defer', 'excluded', 'original', 'newer', 'older-only', 'renamed-away', 'retained-source', 'mapped-helper')) {
        Write-FixtureFile $upstream "src/$name.ts" "export const released = '$name';`n"
    }
    $null = Invoke-FixtureGit $upstream @('add', '.')
    $null = Invoke-FixtureGit $upstream @('commit', '--quiet', '-m', 'released target')
    $target = [string](Invoke-FixtureGit $upstream @('rev-parse', 'HEAD'))
    foreach ($name in @('adopt', 'adapt', 'newer', 'baseline-only', 'baseline-helper', 'mapped-helper')) {
        Write-FixtureFile $upstream "src/$name.ts" "export const newer = '$name';`n"
    }
    Remove-Item -LiteralPath (Join-Path $upstream 'src/older-only.ts'), (Join-Path $upstream 'src/retained-source.ts')
    $null = Invoke-FixtureGit $upstream @('mv', 'src/renamed-away.ts', 'src/renamed-newer.ts')
    $null = Invoke-FixtureGit $upstream @('add', '.')
    $null = Invoke-FixtureGit $upstream @('commit', '--quiet', '-m', 'registered descendant U0')
    $containedBase = [string](Invoke-FixtureGit $upstream @('rev-parse', 'HEAD'))
    foreach ($name in @('newer', 'baseline-only', 'baseline-helper')) {
        Write-FixtureFile $local "dashboard-src/src/$name.ts" "export const newer = '$name';`n"
    }
    Write-FixtureFile $local 'dashboard-src/src/local-name.ts' "export const desktopName = true;`n"
    Write-FixtureFile $local 'dashboard-src/src/mapped-helper.ts' "export const desktopHelper = true;`n"
    Copy-Item -LiteralPath (Join-Path $upstream 'src/renamed-newer.ts') -Destination (Join-Path $local 'dashboard-src/src/renamed-newer.ts')
    Write-FixtureFile $local 'src/Host.cs' 'class Host { }'
    Write-FixtureFile $local '.gitignore' ".tmp/`n"
    Write-FixtureFile $local 'tools/check.ps1' "Write-Host 'fixture check'`n"
    Write-FixtureFile $local 'docs/development.md' '<a id="shared-behavior"></a>'
    $untouchedPending = @{ id = 'unrelated'; paths = @('dashboard-src/src/newer.ts'); reason = 'Separate feature still pending'; reReviewWhen = 'When its desktop behavior is exercised'; dependencies = @() }
    $fixturePolicy = [ordered]@{
        schemaVersion = 2
        upstream = @{ repository = 'https://example.invalid/upstream.git'; tag = 'registered-newer'; revision = $containedBase; localPrefix = 'dashboard-src/' }
        historicalReference = @{ tag = 'unavailable-tag'; revision = ('1' * 40) }
        forkOwnedPaths = @('docs/*', 'tools/*', '.gitignore', 'src/*', 'dashboard-src/src/__tests__/*')
        mirrors = @()
        adaptations = @(
            @{ path = 'dashboard-src/src/local-name.ts'; upstreamPath = 'dashboard-src/src/original.ts'; revision = $containedBase },
            @{ path = 'dashboard-src/src/local-adapt.ts'; upstreamPath = 'dashboard-src/src/adapt.ts'; revision = $containedBase },
            @{ path = 'dashboard-src/src/local-retained.ts'; upstreamPath = 'dashboard-src/src/retained-source.ts'; revision = $target },
            @{ path = 'dashboard-src/src/mapped-helper.ts'; upstreamPath = 'dashboard-src/src/mapped-helper.ts'; revision = $containedBase }
        )
        contracts = @(@{ id = 'shared-behavior'; rule = 'docs/development.md#shared-behavior'; paths = @('dashboard-src/src/*'); watch = @(); checks = @('tools/check.ps1'); risk = 'low' })
        pending = @(
            @{ id = 'missing-feature'; paths = @('dashboard-src/src/adopt.ts'); reason = 'Missing implementation'; reReviewWhen = 'When supplied'; dependencies = @() },
            $untouchedPending
        )
        knownGaps = @()
    }
    Save-FixturePolicy
    $null = Invoke-FixtureGit $local @('add', '.')
    $null = Invoke-FixtureGit $local @('commit', '--quiet', '-m', 'local descendant baseline with gaps')
    Invoke-Review
    $containedPlan = Read-Record 'plan.json'
    $expectedMissing = @('dashboard-src/src/adopt.ts', 'dashboard-src/src/local-adapt.ts', 'dashboard-src/src/defer.ts', 'dashboard-src/src/excluded.ts', 'dashboard-src/src/local-retained.ts')
    Assert-True ($containedPlan.mode -ceq 'supplement' -and (($containedPlan.incoming.path | Sort-Object) -join "`n") -ceq (($expectedMissing | Sort-Object) -join "`n")) 'Contained target did not plan only missing local results through existing mappings.'
    Assert-True ((($containedPlan.requiredPaths | Sort-Object) -join "`n") -ceq (($expectedMissing | Sort-Object) -join "`n")) 'Supplement required rollback of a present newer or renamed file.'
    Assert-True ($containedPlan.renameHints.Count -eq 0 -and $containedPlan.triggeredPending.Count -eq 1 -and $containedPlan.triggeredPending[0].id -ceq 'missing-feature') 'Supplement generated reverse renames or triggered unrelated pending.'
    Assert-True (@((Read-Record 'inventory.json') | Where-Object path -ceq 'dashboard-src/src/older-only.ts').Count -eq 1) 'Full inventory omitted a target file absent from the newer baseline.'
    Assert-Fails { Invoke-Review 'Prepare' } '*Path requires batch coverage*'
    Assert-Fails { Invoke-Review 'Prepare' -AdvanceBaseline } '*AdvanceBaseline is not allowed in supplement mode*'
    $decisions = Read-Record 'decisions.json'
    $decisions.batches = @(
        (New-Batch 'missing-feature' @('dashboard-src/src/adopt.ts') 'adopt'),
        # A present mapping is explicitly reviewed; incoming in localPaths still needs a real U1 result.
        (New-Batch 'adapt-missing' @('dashboard-src/src/local-name.ts') 'adapt'),
        (New-Batch 'later' @('dashboard-src/src/defer.ts') 'defer'),
        (New-Batch 'restore-retained-result' @('dashboard-src/src/local-retained.ts') 'adopt'),
        (New-Batch 'excluded-feature' @('dashboard-src/src/excluded.ts') 'keep-local')
    )
    $decisions.batches[1].localPaths = @('dashboard-src/src/local-adapt.ts')
    $decisions.batches[4].reason = 'Retain the established exclusion of the upstream native API feature'
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*Supplement disposition requires a real local result*'
    Write-FixtureFile $local 'dashboard-src/src/adopt.ts' "export const newer = 'adopt';`n"
    Write-FixtureFile $local 'dashboard-src/src/local-retained.ts' "export const released = 'retained-source';`n"
    Write-FixtureFile $local 'dashboard-src/src/local-adapt.ts' "export const desktopAdapted = true;`n"
    $decisions.batches[2].action = 'defer'
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*Adopt must match exact target source*'
    Write-FixtureFile $local 'dashboard-src/src/adopt.ts' "export const released = 'adopt';`n"
    Remove-Item -LiteralPath (Join-Path $local 'dashboard-src/src/local-adapt.ts')
    Assert-Fails { Invoke-Review 'Prepare' } '*Supplement disposition requires a real local result*'
    $decisions.batches[1].action = 'replace-local'
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*Supplement disposition requires a real local result*'
    Write-FixtureFile $local 'dashboard-src/src/local-adapt.ts' "export const desktopAdapted = true;`n"
    Write-FixtureFile $local 'dashboard-src/src/excluded.ts' 'Unreviewed change to a retained absence'
    Assert-Fails { Invoke-Review 'Prepare' } '*Actual source change is outside adopted/adapted batch paths/localPaths: dashboard-src/src/excluded.ts'
    Remove-Item -LiteralPath (Join-Path $local 'dashboard-src/src/excluded.ts')
    $decisions.batches[2].reason = ''
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*batch.reason*'
    $decisions.batches[2].reason = 'Explicitly leave this released feature pending'
    $decisions.batches[2].reReviewWhen = ''
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*batch.reReviewWhen*'
    $decisions.batches[2].reReviewWhen = 'When the missing feature is implemented'
    Save-Decisions
    $savedContractPaths = $fixturePolicy.contracts[0].paths
    $fixturePolicy.contracts[0].paths = @('dashboard-src/other/*')
    Save-FixturePolicy
    Assert-Fails { Invoke-Review 'Prepare' } '*Adaptation requires a contract*'
    $fixturePolicy.contracts[0].paths = $savedContractPaths
    Save-FixturePolicy
    $companionPaths = @('dashboard-src/src/__tests__/adapt.test.ts', 'src/Host.cs', 'dashboard-src/src/baseline-helper.ts', 'dashboard-src/src/mapped-helper.ts')
    Write-FixtureFile $local $companionPaths[0] "export const localRegression = true;`n"
    Write-FixtureFile $local $companionPaths[1] 'class Host { public bool SupportsAdaptation => true; }'
    Write-FixtureFile $local $companionPaths[2] "export const baselineHelperAdapted = true;`n"
    Write-FixtureFile $local $companionPaths[3] "export const desktopHelperAdapted = true;`n"
    Assert-Fails { Invoke-Review 'Prepare' } '*Actual source change is outside adopted/adapted batch paths/localPaths*'
    $decisions.batches[1].localPaths = @('dashboard-src/src/local-adapt.ts') + $companionPaths + @('dashboard-src/src/unknown-companion.ts')
    Write-FixtureFile $local 'dashboard-src/src/unknown-companion.ts' "export const unknownSource = true;`n"
    Save-Decisions
    Assert-Fails { Invoke-Review 'Prepare' } '*Unclassified result path requires explicit target/adaptation provenance*'
    Remove-Item -LiteralPath (Join-Path $local 'dashboard-src/src/unknown-companion.ts')
    $decisions.batches[1].localPaths = @('dashboard-src/src/local-adapt.ts') + $companionPaths
    Save-Decisions
    Invoke-Review 'Prepare'
    $containedPrepared = Read-Record 'prepared.json'
    foreach ($path in $companionPaths) {
        Assert-True ($path -cin @($containedPrepared.changedPaths.path) -and $path -cin @($containedPrepared.candidateSnapshot.files)) "Declared local companion was omitted from prepared changes or snapshot: $path."
    }
    Assert-Fails { Invoke-Review 'Prepare' -AdvanceBaseline } '*AdvanceBaseline is not allowed in supplement mode*'
    Assert-True ((Read-Record 'prepared.json').preparedHash -ceq $containedPrepared.preparedHash) 'Rejected repeat Prepare altered prepared evidence.'
    $registered = Read-ForkPolicy -Root $local
    Assert-True ($registered.upstream.revision -ceq $containedBase -and $registered.upstream.tag -ceq 'registered-newer') 'Supplement rolled back registered baseline SHA or tag.'
    foreach ($path in @('dashboard-src/src/adopt.ts', 'dashboard-src/src/local-adapt.ts', 'dashboard-src/src/local-name.ts', 'dashboard-src/src/local-retained.ts')) {
        $mapping = @($registered.adaptations | Where-Object path -ceq $path)[0]
        Assert-True ($mapping.revision -ceq $target) "Supplement reused descendant provenance for $path."
    }
    Assert-True (@($registered.adaptations | Where-Object path -ceq 'dashboard-src/src/local-adapt.ts')[0].upstreamPath -ceq 'dashboard-src/src/adapt.ts') 'Supplement lost accurate renamed source mapping.'
    Assert-True (@($registered.adaptations).Count -eq 6 -and -not @($registered.adaptations | Where-Object { $_.path -cin $companionPaths -and $_.revision -ceq $target }).Count) 'Supplement invented target provenance for local companions.'
    Assert-True (@($registered.adaptations | Where-Object path -ceq 'dashboard-src/src/baseline-helper.ts')[0].revision -ceq $containedBase) 'Baseline-only shared companion lost its U0 source.'
    $originalHelperMapping = @($fixturePolicy.adaptations | Where-Object path -ceq 'dashboard-src/src/mapped-helper.ts')[0]
    Assert-True ((ConvertTo-Json -InputObject @($registered.adaptations | Where-Object path -ceq 'dashboard-src/src/mapped-helper.ts')[0] -Compress) -ceq (ConvertTo-Json -InputObject $originalHelperMapping -Compress)) 'Shared companion existing U0 mapping was overwritten by U1.'
    Assert-True (@($registered.adaptations | Where-Object path -ceq 'dashboard-src/src/local-retained.ts')[0].upstreamPath -ceq 'dashboard-src/src/retained-source.ts') 'Retained source lost its explicit historical mapping.'
    Assert-True ('excluded-feature' -cnotin @($registered.pending.id) -and -not (Test-Path -LiteralPath (Join-Path $local 'dashboard-src/src/excluded.ts'))) 'Established exclusion became a defer or silently acquired a local result.'
    Assert-True ((ConvertTo-Json -InputObject @($registered.pending | Where-Object id -ceq 'unrelated')[0] -Compress) -ceq (ConvertTo-Json -InputObject $untouchedPending -Compress)) 'Supplement changed unrelated pending.'
    Assert-True (@($registered.pending.id) -cnotcontains 'missing-feature') 'Adopted missing path did not close its corresponding pending.'
    $deferred = @($registered.pending | Where-Object id -ceq 'later')[0]
    Assert-True ($deferred.paths -ccontains 'dashboard-src/src/defer.ts' -and $deferred.reason -ceq $decisions.batches[2].reason -and $deferred.reReviewWhen -ceq $decisions.batches[2].reReviewWhen) 'Deferred supplement lost its reason or reconsideration condition.'
    Invoke-Review 'Prepare'
    Add-Evidence
    Invoke-Review 'Complete'
    $containedResult = Read-Record 'results.json'
    Assert-True ($containedResult.policy.upstream.revision -ceq $containedBase -and $containedResult.policy.upstream.tag -ceq 'registered-newer' -and -not $containedResult.advanceBaseline) 'Completion did not preserve descendant baseline.'
    foreach ($path in $companionPaths) {
        $snapshotEntry = @($containedResult.candidateSnapshot.manifest | Where-Object path -ceq $path)[0]
        Assert-True ($null -ne $snapshotEntry -and $snapshotEntry.sha256 -ceq (Get-FileHash -LiteralPath (Join-Path $local $path) -Algorithm SHA256).Hash.ToLowerInvariant()) "Completed snapshot did not bind actual local companion content: $path."
    }
    foreach ($name in @('newer', 'baseline-only')) {
        Assert-True ((Get-NormalizedSourceHash (Join-Path $local "dashboard-src/src/$name.ts")) -ceq (Get-ForkUpstreamHash $upstream $containedBase "src/$name.ts")) "Supplement reversed newer content: $name."
    }
    foreach ($name in @('older-only', 'renamed-away')) {
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $local "dashboard-src/src/$name.ts"))) "Supplement reversed a newer baseline deletion or rename: $name."
    }
    Assert-True ((Get-NormalizedSourceHash (Join-Path $local 'dashboard-src/src/renamed-newer.ts')) -ceq (Get-ForkUpstreamHash $upstream $containedBase 'src/renamed-newer.ts')) 'Supplement changed the newer renamed file.'
    Assert-True (([string](Invoke-FixtureGit $upstream @('rev-parse', 'HEAD'))) -ceq $containedBase -and -not @(Invoke-FixtureGit $upstream @('status', '--porcelain')).Count) 'Supplement changed the isolated upstream checkout.'
    Write-Host 'Optional preview, partial pending, decision revisions, target contracts, registration, raw validation binding, contained-target supplementation and one-step immutable completion fixtures passed.'
}
finally {
    if (Test-Path Function:Get-FileHash) { Remove-Item Function:Get-FileHash }
    Get-ChildItem -LiteralPath $fixture -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
    Remove-TemporaryTestDirectory $fixture
}
