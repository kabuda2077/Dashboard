param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$workDir = Join-Path $repoRoot '_build_zashboard'
$zipPath = Join-Path $workDir 'zashboard.zip'
$sourceUrl = 'https://github.com/Zephyruso/zashboard/archive/refs/heads/main.zip'

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Set-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Replace-Required {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not $Content.Contains($Old)) {
        throw "Unable to patch zashboard: pattern not found for $Label."
    }

    return $Content.Replace($Old, $New)
}

if (Test-Path $workDir) {
    Remove-Item -LiteralPath $workDir -Recurse -Force
}

New-Item -ItemType Directory -Path $workDir | Out-Null
Invoke-WebRequest -Uri $sourceUrl -OutFile $zipPath
Expand-Archive -Path $zipPath -DestinationPath $workDir -Force

$sourceDir = Get-ChildItem -Path $workDir -Directory |
    Where-Object { $_.Name -like 'zashboard-*' } |
    Select-Object -First 1

if ($null -eq $sourceDir) {
    throw 'Unable to locate extracted zashboard source directory.'
}

$sourceRoot = $sourceDir.FullName
$corePageSource = Join-Path $repoRoot 'dashboard-native\CorePage.vue'
$corePageTarget = Join-Path $sourceRoot 'src\views\CorePage.vue'
Copy-Item -LiteralPath $corePageSource -Destination $corePageTarget -Force

$hostBootstrapPath = Join-Path $sourceRoot 'src\hostBootstrap.ts'
Set-Utf8File -Path $hostBootstrapPath -Content @'
import { getBackendFromUrl } from '@/helper/utils'
import { addBackend } from '@/store/setup'
import type { Backend } from '@/types'

type HostState = {
  apiUrl?: string
  secret?: string
}

type HostWindow = Window & {
  __mihomoApplyBackend?: (state: HostState) => void
}

const normalizePath = (pathname: string) => {
  const path = pathname.replace(/\/$/, '')
  return path === '' || path === '/' ? '' : path
}

const backendFromApiUrl = (apiUrl: string | undefined, secret: string | undefined) => {
  if (!apiUrl) return null

  try {
    const url = new URL(apiUrl)
    return {
      protocol: url.protocol.replace(':', ''),
      host: url.hostname,
      port: url.port || (url.protocol === 'https:' ? '443' : '80'),
      secondaryPath: normalizePath(url.pathname),
      password: secret || '',
      label: 'Mihomo Dashboard',
      disableUpgradeCore: true,
    } satisfies Omit<Backend, 'uuid'>
  } catch {
    return null
  }
}

const applyBackend = (backend: Omit<Backend, 'uuid'> | null) => {
  if (!backend?.protocol || !backend.host || !backend.port) return
  addBackend(backend)
}

applyBackend(getBackendFromUrl())

;(window as HostWindow).__mihomoApplyBackend = (state) => {
  applyBackend(backendFromApiUrl(state.apiUrl, state.secret))
}
'@

$constantPath = Join-Path $sourceRoot 'src\constant\index.ts'
$constant = [System.IO.File]::ReadAllText($constantPath).Replace("`r`n", "`n")
$constant = Replace-Required $constant "  Cog6ToothIcon,`n  CubeTransparentIcon," "  Cog6ToothIcon,`n  CpuChipIcon,`n  CubeTransparentIcon," 'constant icon import'
$constant = Replace-Required $constant "  overview = 'overview',`n" "  core = 'core',`n  overview = 'overview',`n" 'ROUTE_NAME.core'
$constant = Replace-Required $constant "  [ROUTE_NAME.overview]: CubeTransparentIcon,`n" "  [ROUTE_NAME.core]: CpuChipIcon,`n  [ROUTE_NAME.overview]: CubeTransparentIcon,`n" 'ROUTE_ICON_MAP.core'
Set-Utf8File -Path $constantPath -Content $constant

$routerPath = Join-Path $sourceRoot 'src\router\index.ts'
$router = [System.IO.File]::ReadAllText($routerPath).Replace("`r`n", "`n")
$router = Replace-Required $router "import ConnectionsPage from '@/views/ConnectionsPage.vue'`n" "import ConnectionsPage from '@/views/ConnectionsPage.vue'`nimport CorePage from '@/views/CorePage.vue'`n" 'CorePage import'
$router = Replace-Required $router "const childrenRouter = [`n" "const childrenRouter = [`n  {`n    path: 'core',`n    name: ROUTE_NAME.core,`n    component: CorePage,`n  },`n" 'CorePage route'
$router = Replace-Required $router "  if (!activeBackend.value && to.name !== ROUTE_NAME.setup) {`n" "  if (!activeBackend.value && ![ROUTE_NAME.setup, ROUTE_NAME.core].includes(to.name as ROUTE_NAME)) {`n" 'CorePage route guard'
Set-Utf8File -Path $routerPath -Content $router

$mainPath = Join-Path $sourceRoot 'src\main.ts'
$main = [System.IO.File]::ReadAllText($mainPath).Replace("`r`n", "`n")
$main = Replace-Required $main "import App from './App.vue'`n" "import App from './App.vue'`nimport './hostBootstrap'`n" 'host bootstrap import'
Set-Utf8File -Path $mainPath -Content $main

$translations = @(
    @{ Path = 'src\i18n\en.ts'; Value = 'Core' },
    @{ Path = 'src\i18n\zh.ts'; Value = '\u5185\u6838' },
    @{ Path = 'src\i18n\zh-tw.ts'; Value = '\u6838\u5fc3' },
    @{ Path = 'src\i18n\ru.ts'; Value = 'Core' }
)

foreach ($translation in $translations) {
    $path = Join-Path $sourceRoot $translation.Path
    $content = [System.IO.File]::ReadAllText($path).Replace("`r`n", "`n")
    $content = Replace-Required $content "  // Navigation`n" "  // Navigation`n  core: '$($translation.Value)',`n" "translation $($translation.Path)"
    Set-Utf8File -Path $path -Content $content
}

if ($SkipBuild) {
    Write-Host 'zashboard source patch check completed.'
    return
}

Push-Location $sourceRoot
try {
    pnpm install --frozen-lockfile
    pnpm run build
}
finally {
    Pop-Location
}

$dashboardDir = Join-Path $repoRoot 'resources\dashboard'
if (Test-Path $dashboardDir) {
    Remove-Item -LiteralPath $dashboardDir -Recurse -Force
}

New-Item -ItemType Directory -Path $dashboardDir | Out-Null
Copy-Item -Path (Join-Path $sourceRoot 'dist\*') -Destination $dashboardDir -Recurse -Force

& (Join-Path $repoRoot 'tools\create-app-icon.ps1')
