#requires -Version 7.0
# Offline fixtures: no upstream fetch, package install, browser, or user state.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestPaths.ps1')
. (Join-Path $PSScriptRoot '..\..\tools\internal\fork-policy.ps1')
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$policy = Read-ForkPolicy -Root $repository
Test-ForkMirrors -Root $repository -Policy $policy
$fixture = New-TemporaryTestDirectory 'upstream-audit'
$upstream = Join-Path $fixture 'upstream'
$local = Join-Path $fixture 'local'
$engine = (Get-Process -Id $PID).Path
$auditScript = Join-Path $repository 'tools/internal/audit-upstream.ps1'
$gitExecutable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Git([string]$Directory, [string[]]$Arguments) {
    $result = @(& $gitExecutable -C $Directory @Arguments)
    if ($LASTEXITCODE) { throw "Fixture git failed: $($Arguments -join ' ')" }
    return $result
}
function Put([string]$Root, [string]$Path, [string]$Text) {
    $file = Join-Path $Root $Path
    $null = New-Item -ItemType Directory -Force (Split-Path $file)
    [IO.File]::WriteAllText($file, $Text, [Text.UTF8Encoding]::new($false))
}
function Save-Policy { Put $local 'docs/upstream/local-changes.json' ($script:fixturePolicy | ConvertTo-Json -Depth 12) }
function Run-Audit([bool]$Success, [string]$ExpectedError = '') {
    $output = @(& $engine -NoProfile -File $auditScript -RepositoryRoot $local -UpstreamRoot $upstream -TargetRef $script:u1 2>&1)
    $code = $LASTEXITCODE
    if ($Success) { Assert-True ($code -eq 0) ($output -join "`n") }
    else {
        Assert-True ($code -ne 0) 'Invalid audit input was accepted.'
        Assert-True (($output -join "`n") -like "*$ExpectedError*") "Unexpected failure: $($output -join "`n")"
    }
}
try {
    foreach ($directory in @($upstream, $local)) {
        $null = New-Item -ItemType Directory $directory
        $null = Git $directory @('init', '--quiet')
        $null = Git $directory @('config', 'user.email', 'audit@example.test')
        $null = Git $directory @('config', 'user.name', 'Audit fixture')
        $null = Git $directory @('config', 'core.autocrlf', 'false')
    }
    $files = @('src/common/Control.vue', 'src/views/Page.vue', 'src/retired.ts', 'src/back-to-upstream.ts')
    foreach ($file in $files) { Put $upstream $file "upstream`n" }
    $null = Git $upstream @('add', '.')
    $null = Git $upstream @('commit', '--quiet', '-m', 'U0')
    $u0 = [string](Git $upstream @('rev-parse', 'HEAD'))
    foreach ($file in $files) { Put $local ('dashboard-src/' + $file) "upstream`n" }
    Remove-Item (Join-Path $local 'dashboard-src/src/retired.ts')
    Put $local 'dashboard-src/src/views/Page.vue' "stable adaptation`n"
    Put $local 'dashboard-src/src/back-to-upstream.ts' "stable fork`n"
    Put $local 'src/Host.cs' "stable host`n"
    Put $local '.gitignore' ".tmp/`n"
    Put $local 'checks/behavior.txt' 'fixture check reference'
    $null = Git $local @('add', '.')
    $null = Git $local @('commit', '--quiet', '-m', 'R')
    $null = Git $local @('tag', '-a', 'stable', '-m', 'annotated stable tag')
    $r = [string](Git $local @('rev-parse', 'HEAD'))
    $script:fixturePolicy = [ordered]@{
        schemaVersion = 1
        upstream = @{ tag = 'upstream'; revision = $u0 }
        reference = @{ tag = 'stable'; revision = $r }
        mirrors = @(@{ path = 'dashboard-src/src/common/Control.vue'; sha256 = Get-NormalizedSourceHash (Join-Path $upstream $files[0]) })
        changes = @(@{
            id = 'desktop'; origin = 'desktop'; decision = 'keep-behavior'; intent = 'Fixture desktop contract'; evidence = @('fixture history')
            paths = @('dashboard-src/src/views/*', 'src/*')
            watch = @('dashboard-src/src/common/Control.vue')
            checks = @('checks/behavior.txt')
        })
        pending = @(@{ id = 'density'; change = 'desktop'; question = 'Not approved yet' })
        knownGaps = @(@{ id = 'scroll-gap'; change = 'desktop'; expected = 'Preserve scroll'; observed = 'Unfixed'; coverage = 'not covered' })
    }
    Save-Policy
    Put $local 'dashboard-src/src/common/Control.vue' "upstream`r`n"
    Put $local 'dashboard-src/src/views/Page.vue' "v2 adaptation`n"
    Put $local 'dashboard-src/src/back-to-upstream.ts' "upstream`n"
    Put $local 'dashboard-src/src/untracked.ts' "new local`n"
    Put $local 'src/Host.cs' "v2 host`n"
    # U1 touches a mirrored control (watch trigger), a locally removed file,
    # and an entirely new unclassified feature. U0 remains the clean checkout.
    Put $upstream 'src/common/Control.vue' "new upstream control`n"
    Put $upstream 'src/retired.ts' "changed upstream retired feature`n"
    Put $upstream 'src/new-feature.ts' "new upstream feature`n"
    $null = Git $upstream @('add', '.')
    $null = Git $upstream @('commit', '--quiet', '-m', 'U1')
    $script:u1 = [string](Git $upstream @('rev-parse', 'HEAD'))
    $null = Git $upstream @('checkout', '--quiet', $u0)
    Run-Audit $true
    $reportRoot = Join-Path $local '.tmp/reports/upstream-current'
    $rows = [IO.File]::ReadAllText((Join-Path $reportRoot 'inventory.json')) | ConvertFrom-Json
    $report = [IO.File]::ReadAllText((Join-Path $reportRoot 'audit.json')) | ConvertFrom-Json
    $page = $rows | Where-Object path -eq 'dashboard-src/src/views/Page.vue'
    Assert-True ($page.upstreamToReference -eq 'modified' -and $page.referenceToCandidate -eq 'modified') 'Three-way adaptation was lost.'
    $returned = $rows | Where-Object path -eq 'dashboard-src/src/back-to-upstream.ts'
    Assert-True ($returned.referenceToCandidate -eq 'modified' -and $returned.upstreamToCandidate -eq 'same') 'Returning to upstream must be distinguished from a new fork.'
    $new = $rows | Where-Object path -eq 'dashboard-src/src/untracked.ts'
    Assert-True ($new.referenceToCandidate -eq 'added') 'Untracked candidate source was omitted.'
    $hostChange = $rows | Where-Object path -eq 'src/Host.cs'
    Assert-True ($hostChange.upstreamToReference -eq 'added' -and $hostChange.referenceToCandidate -eq 'modified') 'Native-only changes were omitted or treated as upstream files.'
    Assert-True (@($rows | Where-Object path -eq 'dashboard-src/src/common/Control.vue').Count -eq 0) 'CRLF created a false source difference.'
    $control = $report.incoming | Where-Object path -eq 'dashboard-src/src/common/Control.vue'
    Assert-True (-not $control.localOverlap -and 'desktop' -in $control.changes -and 'checks/behavior.txt' -in $control.checks) 'Unmodified shared control did not trigger consumer checks.'
    Assert-True (($report.incoming | Where-Object path -eq 'dashboard-src/src/retired.ts').localOverlap) 'A locally removed file was treated as safe incoming code.'
    Assert-True (($report.incoming | Where-Object path -eq 'dashboard-src/src/new-feature.ts').attention -eq 'unclassified') 'Unknown feature was silently adopted.'
    Assert-True ($report.candidateDirty -and $report.pendingDecisions.Count -eq 1 -and $report.knownGaps.Count -eq 1) 'Dirty input or unfinished work was hidden.'
    Assert-True ([string](Git $local @('rev-parse', 'HEAD')) -eq $r) 'Audit moved the candidate branch.'

    Put $local 'dashboard-src/src/common/Control.vue' "unreviewed fork`n"
    Run-Audit $false 'Upstream mirror changed'
    $script:fixturePolicy.mirrors[0].sha256 = Get-NormalizedSourceHash (Join-Path $local 'dashboard-src/src/common/Control.vue')
    Save-Policy
    Run-Audit $false 'Mirror baseline is not the reviewed upstream source'
    $script:fixturePolicy.mirrors[0].sha256 = Get-NormalizedSourceHash (Join-Path $upstream $files[0])
    Save-Policy
    Put $local 'dashboard-src/src/common/Control.vue' "upstream`n"
    Put $upstream 'unexpected.txt' 'dirty upstream'
    Run-Audit $false 'upstream worktree must be clean'
    Remove-Item (Join-Path $upstream 'unexpected.txt')
    $script:fixturePolicy.reference.revision = $u0
    Save-Policy
    Run-Audit $false 'stable reference tag does not match'
    $script:fixturePolicy.reference.revision = $r
    $script:fixturePolicy.changes += $script:fixturePolicy.changes[0]
    Save-Policy
    $failed = $false
    try { Read-ForkPolicy -Root $local | Out-Null } catch { $failed = $_.Exception.Message -like '*duplicate change ID*' }
    Assert-True $failed 'Duplicate policy IDs were accepted.'
    Write-Host 'Upstream policy, mirrors, three-way inventory and incoming impact tests passed.'
}
finally {
    # Git creates read-only loose objects on Windows; these are all fixture-owned.
    Get-ChildItem -LiteralPath $fixture -Recurse -Force -File | ForEach-Object { $_.IsReadOnly = $false }
    Remove-TemporaryTestDirectory $fixture
}
