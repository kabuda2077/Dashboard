import {
  configs,
  configsLoaded,
  configsLoadedBackendUuid,
  fetchConfigs,
  updateConfigs,
} from '@/assembly/config'
import { activeBackend } from '@/store/setup'
import { computed, onScopeDispose, ref, watch } from 'vue'

type ConfigStatus = 'idle' | 'loading' | 'ready' | 'error'
type TunStateSource = 'api' | 'host' | 'none'

export type RuntimeTunState = {
  visible: boolean
  writable: boolean
  loading: boolean
  enabled?: boolean
  source: TunStateSource
}

const isConfigLoading = ref(false)
const configError = ref<unknown>(null)
let retryTimer: ReturnType<typeof window.setTimeout> | undefined
let readyRefreshTimer: ReturnType<typeof window.setTimeout> | undefined
const readyRefreshedBackendUuids = new Set<string>()

const clearRetryTimer = () => {
  if (!retryTimer) return
  window.clearTimeout(retryTimer)
  retryTimer = undefined
}

const clearReadyRefreshTimer = () => {
  if (!readyRefreshTimer) return
  window.clearTimeout(readyRefreshTimer)
  readyRefreshTimer = undefined
}

export const useBackendRuntimeConfig = () => {
  const activeBackendUuid = computed(() => activeBackend.value?.uuid || '')
  const isActiveConfigLoaded = computed(
    () => !!activeBackendUuid.value
      && configsLoaded.value
      && configsLoadedBackendUuid.value === activeBackendUuid.value,
  )

  const configStatus = computed<ConfigStatus>(() => {
    if (!activeBackend.value) return 'idle'
    if (configError.value && !isActiveConfigLoaded.value) return 'error'
    return isActiveConfigLoaded.value ? 'ready' : 'loading'
  })

  const hostTunEnabled = computed(() =>
    activeBackend.value?.type === 'singbox'
    && typeof activeBackend.value?.readOnlyTunEnabled === 'boolean'
      ? activeBackend.value.readOnlyTunEnabled
      : undefined,
  )

  const hasWritableApiTun = computed(() =>
    isActiveConfigLoaded.value
    && !!configs.value?.tun
    && !activeBackend.value?.disableTunMode,
  )

  const tunState = computed<RuntimeTunState>(() => {
    if (hasWritableApiTun.value) {
      return {
        visible: true,
        writable: true,
        loading: false,
        enabled: !!configs.value.tun.enable,
        source: 'api',
      }
    }

    if (typeof hostTunEnabled.value === 'boolean') {
      return {
        visible: true,
        writable: false,
        loading: !isActiveConfigLoaded.value && isConfigLoading.value,
        enabled: hostTunEnabled.value,
        source: 'host',
      }
    }

    if (!isActiveConfigLoaded.value && !!activeBackend.value) {
      return {
        visible: true,
        writable: false,
        loading: true,
        source: 'none',
      }
    }

    return {
      visible: false,
      writable: false,
      loading: false,
      source: 'none',
    }
  })

  const scheduleConfigRetry = () => {
    if (retryTimer || !activeBackend.value || isActiveConfigLoaded.value) return
    retryTimer = window.setTimeout(() => {
      retryTimer = undefined
      void ensureConfigLoaded()
    }, 800)
  }

  const scheduleReadyRefresh = () => {
    const backendUuid = activeBackendUuid.value
    if (!backendUuid || activeBackend.value?.type === 'singbox') return
    if (readyRefreshTimer || readyRefreshedBackendUuids.has(backendUuid)) return

    readyRefreshedBackendUuids.add(backendUuid)
    readyRefreshTimer = window.setTimeout(() => {
      readyRefreshTimer = undefined
      if (activeBackendUuid.value !== backendUuid) return
      void fetchConfigs().catch((error) => {
        if (activeBackendUuid.value === backendUuid) {
          configError.value = error
        }
      })
    }, 800)
  }

  const ensureConfigLoaded = async () => {
    if (!activeBackend.value || isActiveConfigLoaded.value || isConfigLoading.value) return
    const requestedBackendUuid = activeBackendUuid.value
    isConfigLoading.value = true
    configError.value = null
    try {
      await fetchConfigs()
    } catch (error) {
      if (activeBackendUuid.value === requestedBackendUuid) {
        configError.value = error
      }
    } finally {
      isConfigLoading.value = false
      if (activeBackend.value && !isActiveConfigLoaded.value) {
        scheduleConfigRetry()
      }
    }
  }

  const updateTunEnabled = async (enabled: boolean) => {
    if (!hasWritableApiTun.value) return
    await updateConfigs({ tun: { enable: enabled } })
  }

  const updateAllowLan = async (enabled: boolean) => {
    if (!isActiveConfigLoaded.value) return
    await updateConfigs({ ['allow-lan']: enabled })
  }

  watch(
    activeBackendUuid,
    () => {
      configError.value = null
      clearRetryTimer()
      clearReadyRefreshTimer()
      void ensureConfigLoaded()
    },
    { immediate: true },
  )

  watch(isActiveConfigLoaded, (loaded) => {
    if (loaded) {
      clearRetryTimer()
      scheduleReadyRefresh()
      return
    }

    scheduleConfigRetry()
  })

  onScopeDispose(() => {
    clearRetryTimer()
    clearReadyRefreshTimer()
  })

  return {
    configError,
    configStatus,
    configs,
    ensureConfigLoaded,
    isActiveConfigLoaded,
    isConfigLoading,
    tunState,
    updateAllowLan,
    updateTunEnabled,
  }
}
