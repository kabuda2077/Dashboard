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
