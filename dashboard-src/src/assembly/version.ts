// 组装层 · 版本与升级。
// mihomo 与 sing-box 都通过 Clash-compatible /version 探测实际内核。
import { fetchClashVersion, restartCoreAPI, upgradeCoreAPI, upgradeUIAPI } from '@/api/clash'
import { hostWindow } from '@/composables/hostBridge'
import { MIHOMO, MIHOMO_CHANNEL } from '@/constant'
import { HOST_BACKEND_UPDATED_EVENT } from '@/constant/hostEvents'
import { autoUpgradeCore, autoUpgradeDashboard, checkUpgradeCore } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import { computed, ref, watch } from 'vue'

export const version = ref()
export const isCoreUpdateAvailable = ref(false)
export const zashboardVersion = ref(__APP_VERSION__)

export const isSingBoxCore = computed(() => version.value?.includes('sing-box'))

export const mihomo = computed<[MIHOMO, string] | undefined>(() => {
  if (isSingBoxCore.value) return undefined
  else {
    const match = /(alpha-smart|alpha|beta|meta)-?(\w+)/.exec(version.value)
    switch (match?.[1]) {
      case 'alpha':
        return [MIHOMO.Alpha, match[2] ?? version.value]
      case 'alpha-smart':
        return [MIHOMO.Smart, match[2] ?? version.value]
      case 'meta':
        return [MIHOMO.Meta, match[2] ?? version.value]
      default:
        return [MIHOMO.Meta, version.value]
    }
  }
})

export const fetchVersionAPI = () => fetchClashVersion()

const getHostCoreVersion = () => hostWindow.__mihomoHostCoreVersion || ''

let versionFetchId = 0

const refreshVersion = async () => {
  if (!activeBackend.value) return

  const currentFetchId = ++versionFetchId
  let nextVersion = ''
  try {
    const { data } = await fetchVersionAPI()
    nextVersion = data?.version || ''
  } catch {
    nextVersion = getHostCoreVersion()
  }

  if (currentFetchId !== versionFetchId) return

  version.value = nextVersion || getHostCoreVersion()
  if (isSingBoxCore.value || !checkUpgradeCore.value || activeBackend.value?.disableUpgradeCore) {
    return
  }

  isCoreUpdateAvailable.value = await fetchBackendUpdateAvailableAPI()

  if (isCoreUpdateAvailable.value && autoUpgradeCore.value) {
    upgradeCoreAPI('auto')
  }
}

watch(
  activeBackend,
  () => {
    void refreshVersion()
  },
  { immediate: true },
)

window.addEventListener(HOST_BACKEND_UPDATED_EVENT, () => {
  void refreshVersion()
})

const CACHE_DURATION = 1000 * 60 * 60

interface CacheEntry<T> {
  timestamp: number
  version: string
  data: T
}

async function fetchWithLocalCache<T>(url: string, version: string): Promise<T> {
  const cacheKey = 'cache/' + url
  const cacheRaw = localStorage.getItem(cacheKey)

  if (cacheRaw) {
    try {
      const cache: CacheEntry<T> = JSON.parse(cacheRaw)
      const now = Date.now()

      if (now - cache.timestamp < CACHE_DURATION && cache.version === version) {
        return cache.data
      } else {
        localStorage.removeItem(cacheKey)
      }
    } catch (e) {
      console.warn('Failed to parse cache for', url, e)
    }
  }

  const response = await fetch(url)
  if (!response.ok) {
    throw new Error(`Fetch failed: ${response.status} ${response.statusText}`)
  }

  const data: T = await response.json()
  const newCache: CacheEntry<T> = {
    timestamp: Date.now(),
    version,
    data,
  }

  localStorage.setItem(cacheKey, JSON.stringify(newCache))
  return data
}

export const fetchIsUIUpdateAvailable = async () => {
  const { tag_name } = await fetchWithLocalCache<{ tag_name: string }>(
    'https://api.github.com/repos/Zephyruso/zashboard/releases/latest',
    zashboardVersion.value,
  )

  return Boolean(tag_name && tag_name !== `v${zashboardVersion.value}`)
}

const check = async (url: string, versionNumber: string) => {
  const { assets } = await fetchWithLocalCache<{ assets: { name: string }[] }>(url, versionNumber)
  const alreadyLatest = assets.some(({ name }) => name.includes(versionNumber))

  return !alreadyLatest
}

export const fetchBackendUpdateAvailableAPI = async () => {
  return await check(
    MIHOMO_CHANNEL[mihomo.value?.[0] ?? MIHOMO.Meta].check_update_url,
    mihomo.value?.[1] ?? version.value,
  )
}

// 仪表盘(UI)更新检查,迁自 composables/settings.ts 的 useSettings。
export const isUIUpdateAvailable = ref(false)

export const checkUIUpdate = async () => {
  isUIUpdateAvailable.value = await fetchIsUIUpdateAvailable()
  if (isUIUpdateAvailable.value && autoUpgradeDashboard.value) {
    upgradeUIAPI()
  }
}

// 内核 / UI 维护动作(Clash 专属,无后端分支),经版本域门面暴露给 view。
export { restartCoreAPI, upgradeCoreAPI, upgradeUIAPI }
