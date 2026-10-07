#requires -Version 7.0
param(
    [string]$MihomoTag = 'v1.19.31',
    [string]$SingBoxTag = 'v1.14.2-reF1nd'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$dest = Join-Path $root '.tmp\tests\fixtures\validation-cores'
New-Item -ItemType Directory -Force $dest | Out-Null
$headers = @{ 'User-Agent' = 'Dashboard-Integration-Validation' }
$sources = @(
    @{ Kind = 'mihomo'; Uri = ('https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/' + [Uri]::EscapeDataString($MihomoTag)); Asset = '^mihomo-windows-amd64-compatible-.*\.zip$'; Exe = 'mihomo.exe' },
    @{ Kind = 'sing-box'; Uri = ('https://api.github.com/repos/reF1nd/sing-box-releases/releases/tags/' + [Uri]::EscapeDataString($SingBoxTag)); Asset = '^sing-box-.*-windows-amd64v3\.zip$'; Exe = 'sing-box.exe' }
)
$records = foreach ($source in $sources) {
    $releases = Invoke-RestMethod $source.Uri -Headers $headers
    $release = @($releases | Where-Object { -not $_.draft })[0]
    $asset = @($release.assets | Where-Object name -Match $source.Asset)[0]
    if (-not $asset -or $asset.digest -notmatch '^sha256:([0-9a-fA-F]{64})$') { throw 'Validation cores require a published SHA256 digest.' }
    $digest = $Matches[1].ToLowerInvariant()
    $directory = Join-Path $dest $source.Kind
    New-Item -ItemType Directory -Force $directory | Out-Null
    $zip = Join-Path $directory 'download.zip'
    if (-not (Test-Path $zip) -or (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest) {
        Invoke-WebRequest $asset.browser_download_url -Headers $headers -OutFile $zip -UseBasicParsing
    }
    if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $digest) { throw 'Core download digest mismatch.' }
    $extract = Join-Path $directory 'extract'
    if (Test-Path $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
    Expand-Archive $zip $extract
    $exe = @(Get-ChildItem $extract -Filter '*.exe' -Recurse)[0]
    if (-not $exe) { throw 'Downloaded archive has no executable.' }
    $path = Join-Path $directory $source.Exe
    Copy-Item -LiteralPath $exe.FullName -Destination $path -Force
    [ordered]@{ kind = $source.Kind; tag = $release.tag_name; asset = $asset.name; digest = $digest; executable = $path; source = $asset.browser_download_url }
}
$records | ConvertTo-Json | Out-File (Join-Path $dest 'provenance.json') -Encoding utf8
Write-Host 'Downloaded verified test cores only. No user core or system setting was modified.'
