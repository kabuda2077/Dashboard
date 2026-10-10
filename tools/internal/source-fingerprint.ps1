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

function Get-ReleaseSourceSnapshot {
    param([string]$Root)
    $head = [string](git -C $Root rev-parse HEAD)
    if ($LASTEXITCODE) { throw 'Cannot identify release source revision.' }
    $status = @(git -C $Root status --porcelain)
    if ($LASTEXITCODE) { throw 'Cannot read release source status.' }
    $inventory = @(git -C $Root -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE) { throw 'Cannot enumerate release source inputs.' }
    # These tracked icons are generated from the tracked favicon and icon script.
    $files = [string[]]@($inventory | Where-Object { $_ -notin @('resources/app.ico', 'resources/tray.ico') } | Select-Object -Unique)
    [Array]::Sort($files, [StringComparer]::Ordinal)
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

function Get-FrontendSnapshot {
    param([string]$Root)
    $tracked = @(git -C $Root -c core.quotepath=false ls-files --cached --others --exclude-standard -- dashboard-src)
    if ($LASTEXITCODE) { throw 'Cannot fingerprint frontend source inventory.' }
    # Culture sorting differs between .NET Framework and modern .NET. Fingerprints
    # must use an explicit path order independent of the shell's locale/runtime.
    $unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in $tracked) {
        if (Test-Path -LiteralPath (Join-Path $Root $file) -PathType Leaf) { $null = $unique.Add($file) }
    }
    $files = [string[]]@($unique)
    [Array]::Sort($files, [StringComparer]::Ordinal)
    $lines = @($files | ForEach-Object { $_.Substring('dashboard-src/'.Length) + "`t" + (Get-NormalizedSourceHash (Join-Path $Root $_)) })
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $digest = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n"))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
    return [pscustomobject]@{ fileCount = $files.Count; treeSha256 = $digest }
}
