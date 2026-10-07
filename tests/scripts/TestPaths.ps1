#requires -Version 7.0
$script:TestRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$script:TestRunRoot = Join-Path $script:TestRepositoryRoot ('.tmp\tests\ps-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$script:OwnedTestDirectories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function New-TemporaryTestDirectory {
    param([string]$Name = 'test')
    if ($Name -notmatch '^[A-Za-z0-9_-]+$') { throw 'Use a simple test-directory name.' }
    $path = Join-Path $script:TestRunRoot ($Name + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $null = $script:OwnedTestDirectories.Add($path)
    return $path
}

function Get-TestReportDirectory {
    param([string]$Name)
    if ($Name -notmatch '^[A-Za-z0-9_-]+$') { throw 'Use one report-directory name.' }
    $path = Join-Path $script:TestRepositoryRoot ('.tmp\reports\' + $Name)
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

function Remove-TestTreeWithoutFollowingLinks {
    param([string]$Path)
    if (-not [IO.Path]::Exists($Path)) { return }
    $directory = [IO.DirectoryInfo]::new($Path)
    if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $directory.Delete(); return }
    foreach ($entry in $directory.EnumerateFileSystemInfos()) {
        if (($entry.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
            Remove-TestTreeWithoutFollowingLinks -Path $entry.FullName
        } else { $entry.Delete() }
    }
    $directory.Delete()
}

function Remove-TemporaryTestDirectory {
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $script:OwnedTestDirectories.Contains($full)) { throw "Refusing cleanup of an unowned directory: $full" }
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            Remove-TestTreeWithoutFollowingLinks $full
            $null = $script:OwnedTestDirectories.Remove($full)
            try { if ([IO.Directory]::Exists($script:TestRunRoot)) { [IO.Directory]::Delete($script:TestRunRoot, $false) } }
            catch [IO.IOException] { } # another owned test directory may still exist
            return
        }
        catch {
            $failure = $_
            if ($attempt -lt 19) { Start-Sleep -Milliseconds 100 }
        }
    }
    $report = Join-Path (Get-TestReportDirectory 'cleanup') ('powershell-' + [Guid]::NewGuid().ToString('N') + '.json')
    [ordered]@{ path = $full; error = $failure.ToString(); recordedAt = [DateTimeOffset]::UtcNow.ToString('o'); processId = $PID } |
        ConvertTo-Json | Set-Content -LiteralPath $report -Encoding utf8
    throw "Test cleanup failed: $full. Details: $report"
}
