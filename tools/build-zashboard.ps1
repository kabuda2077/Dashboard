param(
    [switch]$SkipBuild,
    [ValidateSet('all', 'cdn', 'firasans', 'misans', 'none', 'pingfang', 'sarasa')]
    [string]$Font = 'misans'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sourceRoot = Join-Path $repoRoot 'dashboard-src'
$dashboardDir = Join-Path $repoRoot 'resources\dashboard'

function Show-DashboardAssetStats {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    $assetsDir = Join-Path $Path 'assets'
    if (-not (Test-Path -LiteralPath $assetsDir)) {
        return
    }

    $files = Get-ChildItem -LiteralPath $assetsDir -File
    $total = ($files | Measure-Object Length -Sum).Sum
    $js = ($files | Where-Object { $_.Extension -eq '.js' } | Measure-Object Length -Sum).Sum
    $css = ($files | Where-Object { $_.Extension -eq '.css' } | Measure-Object Length -Sum).Sum
    $fontBytes = ($files | Where-Object { $_.Extension -in '.woff', '.woff2', '.ttf' } | Measure-Object Length -Sum).Sum

    Write-Host ("dashboard asset stats: total={0:N2}MB js={1:N2}MB css={2:N2}MB fonts={3:N2}MB files={4}" -f `
        ($total / 1MB), ($js / 1MB), ($css / 1MB), ($fontBytes / 1MB), $files.Count)
}

if (-not (Test-Path (Join-Path $sourceRoot 'package.json'))) {
    throw "dashboard-src is missing. Restore the zashboard source before building."
}

$requiredFiles = @(
    'src\hostBootstrap.ts',
    'src\composables\hostBridge.ts',
    'src\views\CorePage.vue',
    'src\router\index.ts',
    'src\constant\index.ts'
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $sourceRoot $relativePath
    if (-not (Test-Path $path)) {
        throw "dashboard source check failed: missing $relativePath"
    }
}

$forbiddenFiles = @(
    'src\views\SettingsPage.vue',
    'src\components\controls\SettingsCtrl.vue',
    'src\components\settings\backend\DnsQuery.vue'
)

foreach ($relativePath in $forbiddenFiles) {
    $path = Join-Path $sourceRoot $relativePath
    if (Test-Path -LiteralPath $path) {
        throw "dashboard source check failed: $relativePath should not be restored in the desktop build"
    }
}

$mainTsPath = Join-Path $sourceRoot 'src\main.ts'
$mainTs = Get-Content -LiteralPath $mainTsPath -Raw
if ($mainTs -notmatch "import\s+['""]\./hostBootstrap['""]") {
    throw "dashboard source check failed: src\main.ts must import ./hostBootstrap for desktop window drag/resize and host state"
}

$sidebarButtonsPath = Join-Path $sourceRoot 'src\components\sidebar\SidebarButtons.vue'
$sidebarButtons = Get-Content -LiteralPath $sidebarButtonsPath -Raw
foreach ($pattern in @('showBackendSettingsDialog', 'BackendSettings', 'ServerIcon')) {
    if ($sidebarButtons -match $pattern) {
        throw "dashboard source check failed: sidebar backend settings button should not be restored"
    }
}

$commonCtrlPath = Join-Path $sourceRoot 'src\components\sidebar\CommonCtrl.vue'
$commonCtrl = Get-Content -LiteralPath $commonCtrlPath -Raw
if ($commonCtrl -match 'BackendVersion') {
    throw "dashboard source check failed: sidebar backend version should not be restored"
}

$overviewCtrlPath = Join-Path $sourceRoot 'src\components\controls\OverviewCtrl.vue'
$overviewCtrl = Get-Content -LiteralPath $overviewCtrlPath -Raw
foreach ($pattern in @('BackendVersion', 'getLabelFromBackend', 'activeBackend')) {
    if ($overviewCtrl -match $pattern) {
        throw "dashboard source check failed: overview top bar should not restore backend switch/version text"
    }
}

$hostBridgePath = Join-Path $sourceRoot 'src\composables\hostBridge.ts'
$hostBridge = Get-Content -LiteralPath $hostBridgePath -Raw
if ($hostBridge -notmatch 'isAutostartUpdating\?: boolean') {
    throw "dashboard source check failed: host state must expose isAutostartUpdating"
}

$corePagePath = Join-Path $sourceRoot 'src\views\CorePage.vue'
$corePage = Get-Content -LiteralPath $corePagePath -Raw
foreach ($pattern in @(':disabled="isAutostartUpdating"', 'state.isAutostartUpdating')) {
    if ($corePage -notmatch [regex]::Escape($pattern)) {
        throw "dashboard source check failed: CorePage autostart control must follow the authoritative host update state"
    }
}
foreach ($pattern in @('OverviewCardSettingsDialog')) {
    if ($overviewCtrl -notmatch [regex]::Escape($pattern)) {
        throw "dashboard source check failed: overview top bar must keep card settings"
    }
}

$packageJson = Get-Content -LiteralPath (Join-Path $sourceRoot 'package.json') -Raw
foreach ($pattern in @('@bufbuild/protobuf', '@connectrpc/connect', '@connectrpc/connect-web', '@xterm/xterm')) {
    if ($packageJson -match [regex]::Escape($pattern)) {
        throw "dashboard source check failed: desktop build must not restore sing-box native API dependencies"
    }
}
foreach ($relativePath in @('src\api\singbox', 'src\gen\daemon', 'src\components\tools', 'src\views\ToolsPage.vue')) {
    $nativePath = Join-Path $sourceRoot $relativePath
    $hasNativeSource = (Test-Path -LiteralPath $nativePath -PathType Leaf) -or `
        ((Test-Path -LiteralPath $nativePath -PathType Container) -and `
            @(Get-ChildItem -LiteralPath $nativePath -Recurse -File).Count -gt 0)
    if ($hasNativeSource) {
        throw "dashboard source check failed: desktop build must not restore sing-box native API or Tools sources"
    }
}

$networkCardPath = Join-Path $sourceRoot 'src\components\overview\NetworkCard.vue'
$networkCard = Get-Content -LiteralPath $networkCardPath -Raw
foreach ($pattern in @('network-card', 'network-card-grid', 'network-card-status', '@container (min-width: 768px)', 'grid-template-columns: repeat(3', 'grid-column: span 2', 'ConnectionStatus', 'IPCheck')) {
    if ($networkCard -notmatch [regex]::Escape($pattern)) {
        throw "dashboard source check failed: overview NetworkCard must use container-query layout with Latency across two columns and Network Info across one column"
    }
}

$connectionStatusPath = Join-Path $sourceRoot 'src\components\overview\ConnectionStatus.vue'
$connectionStatus = Get-Content -LiteralPath $connectionStatusPath -Raw
foreach ($pattern in @('const ROUNDS = 10', 'LatencyChart', 'label: ''min''', 'label: ''max''')) {
    if ($connectionStatus -notmatch [regex]::Escape($pattern)) {
        throw "dashboard source check failed: overview latency card must keep 10-sample LatencyChart with min/max metadata"
    }
}

$latencyChartPath = Join-Path $sourceRoot 'src\components\overview\LatencyChart.vue'
if (-not (Test-Path -LiteralPath $latencyChartPath)) {
    throw "dashboard source check failed: missing overview LatencyChart.vue"
}
$latencyChart = Get-Content -LiteralPath $latencyChartPath -Raw
foreach ($pattern in @('MiniSparkline', 'show-symbols', 'useTooltip', 'sampleHits', 'avgLatency')) {
    if ($latencyChart -notmatch [regex]::Escape($pattern)) {
        throw "dashboard source check failed: overview LatencyChart must reuse MiniSparkline, show samples, and keep compact per-sample tooltip"
    }
}

$miniSparklinePath = Join-Path $sourceRoot 'src\components\overview\MiniSparkline.vue'
$miniSparkline = Get-Content -LiteralPath $miniSparklinePath -Raw
foreach ($pattern in @('showSymbols', 'lowLatency', 'mediumLatency', 'highLatency', 'symbolSize: 3')) {
    if ($miniSparkline -notmatch [regex]::Escape($pattern)) {
        throw "dashboard source check failed: MiniSparkline must keep latency colors and optional sample symbols"
    }
}

$zashboardSettingsPath = Join-Path $sourceRoot 'src\components\settings\general\ZashboardSettings.vue'
$zashboardSettings = Get-Content -LiteralPath $zashboardSettingsPath -Raw
foreach ($pattern in @('zashboardVersion', '__COMMIT_ID__', 'github.com/Zephyruso/zashboard', 'isUIUpdateAvailable')) {
    if ($zashboardSettings -match $pattern) {
        throw "dashboard source check failed: settings title must stay localized without version or upstream link"
    }
}
if ($zashboardSettings -notmatch 'zashboardSettings') {
    throw "dashboard source check failed: settings title must use the zashboardSettings i18n key"
}

$generalSettingsPath = Join-Path $sourceRoot 'src\components\settings\general\GeneralSettings.vue'
$generalSettings = Get-Content -LiteralPath $generalSettingsPath -Raw
foreach ($pattern in @('upgradeUIAPI', 'handlerClickUpgradeUI', 'autoUpgradeDashboard', 'upgradeDashboard')) {
    if ($generalSettings -match $pattern) {
        throw "dashboard source check failed: dashboard UI upgrade controls should not be restored"
    }
}

$forbiddenSourcePatterns = @(
    'DnsQuery',
    'DNSQuery',
    'queryDNSAPI',
    'dns-query',
    'dnsQuery',
    'SINGBOX_NATIVE',
    'SingBoxNative'
)

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'src') -Recurse -File |
    Where-Object { $_.Extension -in '.ts', '.tsx', '.vue' }

foreach ($pattern in $forbiddenSourcePatterns) {
    $match = $sourceFiles |
        Select-String -Pattern $pattern -SimpleMatch |
        Select-Object -First 1
    if ($match) {
        throw "dashboard source check failed: forbidden pattern '$pattern' found in $($match.Path)"
    }
}

$mainCssPath = Join-Path $sourceRoot 'src\assets\main.css'
$desktopCssPath = Join-Path $sourceRoot 'src\assets\styles\dashboard-desktop.css'

if (-not (Test-Path -LiteralPath $mainCssPath)) {
    throw "dashboard source check failed: missing src\assets\main.css"
}

if (-not (Test-Path -LiteralPath $desktopCssPath)) {
    throw "dashboard source check failed: missing src\assets\styles\dashboard-desktop.css"
}

$mainCss = Get-Content -LiteralPath $mainCssPath -Raw
$desktopCss = Get-Content -LiteralPath $desktopCssPath -Raw

$imports = [regex]::Matches($mainCss, "@import\s+['""]([^'""]+)['""]\s*;") |
    ForEach-Object { $_.Groups[1].Value }

if (-not $imports -or $imports[-1] -ne './styles/dashboard-desktop.css') {
    throw "dashboard source check failed: dashboard-desktop.css must be the last import in src\assets\main.css"
}

$requiredSelectors = @(
    '.settings-section-label',
    '.dashboard-section-title',
    '.settings-grid',
    '.setting-item',
    '.setting-panel-row',
    '.dashboard-input',
    '.dashboard-action-btn',
    '.dashboard-note',
    '.dashboard-log-block',
    '.core-status-box',
    '.core-top-button',
    '.toggle',
    '.ctrls-bar'
)

$missingSelectors = @()
foreach ($selector in $requiredSelectors) {
    if ($desktopCss -notmatch [regex]::Escape($selector)) {
        $missingSelectors += $selector
    }
}

if ($missingSelectors.Count -gt 0) {
    throw "dashboard source check failed: missing desktop selectors: $($missingSelectors -join ', ')"
}

Write-Host 'dashboard source contract check completed.'

if ($SkipBuild) {
    Write-Host 'dashboard source check completed.'
    return
}

$pnpmCommand = Get-Command pnpm -ErrorAction SilentlyContinue
$pnpmPath = if ($pnpmCommand) { $pnpmCommand.Source } else { Join-Path $env:APPDATA 'npm\pnpm.cmd' }
if (-not (Test-Path $pnpmPath)) {
    throw "pnpm is required for local dashboard builds. Install pnpm 11.20.0, for example: npm install -g pnpm@11.20.0"
}

Push-Location $sourceRoot
try {
    $pnpmStoreDir = Join-Path $repoRoot '.tmp\pnpm-store'
    New-Item -ItemType Directory -Force -Path $pnpmStoreDir | Out-Null
    $env:PNPM_HOME = if ($env:PNPM_HOME) { $env:PNPM_HOME } else { Join-Path $env:APPDATA 'pnpm' }
    $env:PNPM_STORE_DIR = $pnpmStoreDir
    $env:npm_config_store_dir = $pnpmStoreDir

    & $pnpmPath install --frozen-lockfile --store-dir $pnpmStoreDir
    if ($LASTEXITCODE -ne 0) {
        throw "pnpm install failed with exit code $LASTEXITCODE"
    }

    $vitePath = Join-Path $sourceRoot 'node_modules\.bin\vite.cmd'
    if (-not (Test-Path $vitePath)) {
        throw "vite is missing. Run pnpm install in dashboard-src."
    }

    $previousFont = $env:FONT
    $previousDesktopBuild = $env:DESKTOP_BUILD
    $env:FONT = $Font
    $env:DESKTOP_BUILD = '1'

    & $vitePath build
    if ($LASTEXITCODE -ne 0) {
        throw "dashboard build failed with exit code $LASTEXITCODE"
    }
}
finally {
    $env:FONT = $previousFont
    $env:DESKTOP_BUILD = $previousDesktopBuild
    Pop-Location
}

if (Test-Path $dashboardDir) {
    Remove-Item -LiteralPath $dashboardDir -Recurse -Force
}

New-Item -ItemType Directory -Path $dashboardDir | Out-Null
Copy-Item -Path (Join-Path $sourceRoot 'dist\*') -Destination $dashboardDir -Recurse -Force
Show-DashboardAssetStats -Path $dashboardDir

& (Join-Path $repoRoot 'tools\create-app-icon.ps1')
