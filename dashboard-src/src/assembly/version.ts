// 组装层 · 版本与升级。
// mihomo 与 sing-box 都通过 Clash-compatible /version 探测实际内核。
import { fetchClashVersion, restartCoreAPI, upgradeCoreAPI } from '@/api/clash'
import { hasHostBridge, hostState } from '@/composables/hostBridge'
import { backendSessionGeneration, backendSessionReady, captureBackendSession } from '@/helper/backendSession'
import { MIHOMO, MIHOMO_CHANNEL } from '@/constant'
import { HOST_BACKEND_UPDATED_EVENT } from '@/constant/hostEvents'
import { autoUpgradeCore, checkUpgradeCore } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import { computed, ref, watch } from 'vue'

export const version = ref()
export const isCoreUpdateAvailable = ref(false)

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

const getHostCoreVersion = () => hostState.value.coreVersion || ''

let versionFetchId = 0

const refreshVersion = async () => {
  const currentFetchId = ++versionFetchId
  const session = captureBackendSession()
  if (!activeBackend.value || !backendSessionReady.value) {
    version.value = ''
    isCoreUpdateAvailable.value = false
    return
  }

  let nextVersion = ''
  try {
    const { data } = await fetchVersionAPI()
    nextVersion = data?.version || ''
  } catch {
    nextVersion = getHostCoreVersion()
  }

  if (currentFetchId !== versionFetchId || !session.isCurrent()) return

  version.value = nextVersion || getHostCoreVersion()
  isCoreUpdateAvailable.value = false
  if (isSingBoxCore.value || !checkUpgradeCore.value || activeBackend.value?.disableUpgradeCore) {
    return
  }

  try {
    const available = await fetchBackendUpdateAvailableAPI()
    if (currentFetchId !== versionFetchId || !session.isCurrent()) return
    isCoreUpdateAvailable.value = available
    if (isCoreUpdateAvailable.value && autoUpgradeCore.value) {
      await upgradeCoreAPI('auto')
    }
  } catch {
    if (currentFetchId === versionFetchId && session.isCurrent()) isCoreUpdateAvailable.value = false
  }
}

watch(
  () => [backendSessionGeneration.value, backendSessionReady.value],
  () => {
    void refreshVersion()
  },
  { immediate: true },
)

// An exe-version result can arrive after the API probe failed. Fill the
// fallback without issuing another /version request for an unchanged session.
watch(() => hostState.value.coreVersion, (fallback) => {
  if (backendSessionReady.value && !version.value && fallback) version.value = fallback
})

window.addEventListener(HOST_BACKEND_UPDATED_EVENT, () => {
  if (!hasHostBridge) void refreshVersion()
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
export { restartCoreAPI, upgradeCoreAPI }
