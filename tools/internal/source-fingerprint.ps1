#requires -Version 7.0
function Get-NormalizedSourceHash {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return 'MISSING' }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    if ($text.IndexOf([char]0) -lt 0 -and $text.IndexOf([char]0xfffd) -lt 0) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($text.Replace("`r`n", "`n"))
    }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
}

function Get-SourceInputPaths {
    param([string]$Root)
    $inventory = @(git --no-replace-objects -C $Root -c core.quotepath=false ls-files --cached --others --exclude-standard -z)
    if ($LASTEXITCODE) { throw 'Cannot enumerate source inputs.' }
    $paths = ($inventory -join "`n").Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)
    # These tracked icons are generated from the tracked favicon and icon script.
    $files = [string[]]@($paths | Where-Object { $_ -notin @('resources/app.ico', 'resources/tray.ico') } | Select-Object -Unique)
    [Array]::Sort($files, [StringComparer]::Ordinal)
    return $files
}

function Get-ReleaseSourceSnapshot {
    param([string]$Root)
    $head = [string](git -C $Root rev-parse HEAD)
    if ($LASTEXITCODE) { throw 'Cannot identify release source revision.' }
    $status = @(git -C $Root status --porcelain)
    if ($LASTEXITCODE) { throw 'Cannot read release source status.' }
    $files = @(Get-SourceInputPaths -Root $Root)
    $manifest = @($files | ForEach-Object {
        $path = Join-Path $Root $_
        $hash = if (Test-Path -LiteralPath $path -PathType Leaf) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() } else { 'MISSING' }
        "$_`t$hash"
    })
    return [pscustomobject]@{ head = $head; dirty = ($status.Count -gt 0); manifest = $manifest }
}

function Assert-ReleaseSourceSnapshot {
    param([string]$Root, $Snapshot)
    $current = Get-ReleaseSourceSnapshot -Root $Root
    if ($current.head -cne $Snapshot.head -or
        ($current.manifest -join "`n") -cne ($Snapshot.manifest -join "`n")) {
        throw 'Release source inputs changed during verification or packaging; the previous ZIP is preserved. Run release again with stable inputs.'
    }
}
