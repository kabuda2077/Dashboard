// 组装层 · 版本与升级。
// mihomo 与 sing-box 都通过 Clash-compatible /version 探测实际内核。
import { fetchClashVersion, restartCoreAPI, upgradeCoreAPI } from '@/api/clash'
import { hasHostBridge, hostState } from '@/composables/hostBridge'
import {
  backendSessionGeneration,
  backendSessionReady,
  captureBackendSession,
} from '@/helper/backendSession'
import { autoUpgradeCore, checkUpgradeCore } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import { computed, ref, watch } from 'vue'

export const version = ref()
export const isCoreUpdateAvailable = ref(false)

export const resolveCoreUpdateSource = (value: string, singBox: boolean) => {
  const trimmed = value.trim()
  const prefix = singBox
    ? 'sing-box(?: version)?'
    : '(?:Mihomo(?: (?:Meta|Smart))?|Clash(?:\\.Meta)?)'
  const match = new RegExp(`^${prefix}[ \\t]+(\\S+)(?:\\s|$)`, 'i').exec(trimmed)
  const token = match?.[1] ?? (/^\S+$/.test(trimmed) ? trimmed : '')
  const manual = (releaseUrl: string) => ({ mode: 'manual' as const, releaseUrl })
  if (singBox) {
    if (/(?:^|-)reF1nd(?:[.-]|$)/i.test(token))
      return manual('https://github.com/reF1nd/sing-box-releases/releases')
    if (/^v?\d+\.\d+\.\d+(?:-(?:alpha|beta|rc)\.\d+)?$/i.test(token))
      return manual('https://github.com/SagerNet/sing-box/releases')
  } else {
    if (/^TanakaLun-/i.test(token)) return manual('https://github.com/TanakaLun/mihomo/releases')
    if (/^alpha-smart/i.test(token) || /^Mihomo Smart\s+/i.test(trimmed))
      return manual('https://github.com/vernesong/mihomo/releases')
    if (/^(?:v\d+\.\d+\.\d+|(?:alpha|beta|meta)-[a-f\d]{6,40})$/i.test(token))
      return {
        mode: 'automatic' as const,
        releaseUrl: 'https://github.com/MetaCubeX/mihomo/releases',
      }
  }
  return { mode: 'unknown' as const, releaseUrl: '' }
}

export const coreUpdateSource = computed(() =>
  hasHostBridge
    ? {
        mode: hostState.value.coreUpdateMode ?? 'unknown',
        releaseUrl: hostState.value.coreReleaseUrl ?? '',
      }
    : resolveCoreUpdateSource(version.value ?? '', isSingBoxCore.value),
)

export const repositoryUrlFor = (releaseUrl: string) =>
  releaseUrl === 'https://github.com/reF1nd/sing-box-releases/releases'
    ? 'https://github.com/reF1nd/sing-box'
    : releaseUrl.replace(/\/releases$/, '')

export const coreRepositoryUrl = computed(() => repositoryUrlFor(coreUpdateSource.value.releaseUrl))

export const isSingBoxCore = computed(() =>
  hasHostBridge ? hostState.value.coreType === 'sing-box' : version.value?.includes('sing-box'),
)

export const fetchVersionAPI = () => fetchClashVersion()

let versionFetchId = 0

const refreshVersion = async () => {
  if (hasHostBridge) {
    version.value = hostState.value.coreVersion ?? ''
    isCoreUpdateAvailable.value =
      hostState.value.coreUpdateMode === 'automatic' && !!hostState.value.coreUpdateAvailable
    return
  }
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
    // A browser session has no host version to fall back to.
    nextVersion = ''
  }

  if (currentFetchId !== versionFetchId || !session.isCurrent()) return

  version.value = nextVersion
  isCoreUpdateAvailable.value = false
  if (
    coreUpdateSource.value.mode !== 'automatic' ||
    !checkUpgradeCore.value ||
    activeBackend.value?.disableUpgradeCore
  ) {
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
    if (currentFetchId === versionFetchId && session.isCurrent())
      isCoreUpdateAvailable.value = false
  }
}

watch(
  () => [
    backendSessionGeneration.value,
    backendSessionReady.value,
    hostState.value.coreVersion,
    hostState.value.coreUpdateAvailable,
    hostState.value.coreUpdateMode,
  ],
  () => {
    void refreshVersion()
  },
  { immediate: true },
)

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
  if (coreUpdateSource.value.mode !== 'automatic') return false
  const build = /^(?:alpha|beta|meta)-([a-f\d]{6,40})$/i.exec(version.value ?? '')
  return await check(
    build
      ? 'https://api.github.com/repos/MetaCubeX/mihomo/releases/tags/Prerelease-Alpha'
      : 'https://api.github.com/repos/MetaCubeX/mihomo/releases/latest',
    build?.[1] ?? version.value,
  )
}
export { restartCoreAPI, upgradeCoreAPI }
