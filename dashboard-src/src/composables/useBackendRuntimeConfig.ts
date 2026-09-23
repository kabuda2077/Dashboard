import {
  configs,
  configsLoaded,
  configsLoadedBackendUuid,
  fetchConfigs,
  getConfigsGeneration,
  resetConfigs,
  updateConfigs,
} from '@/assembly/config'
import { HOST_BACKEND_UPDATED_EVENT } from '@/constant/hostEvents'
import { activeBackend } from '@/store/setup'
import { hasHostBridge } from '@/composables/hostBridge'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
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
let loadingRequestKey = ''
let retryTimer: ReturnType<typeof window.setTimeout> | undefined
let readyRefreshTimer: ReturnType<typeof window.setTimeout> | undefined
const readyRefreshedRequestKeys = new Set<string>()

const getConfigRequestKey = (backendUuid: string) => `${backendUuid}:${getConfigsGeneration()}:${backendSessionGeneration.value}`

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
    () =>
      !!activeBackendUuid.value &&
      configsLoaded.value &&
      configsLoadedBackendUuid.value === activeBackendUuid.value,
  )

  const configStatus = computed<ConfigStatus>(() => {
    if (!activeBackend.value) return 'idle'
    if (configError.value && !isActiveConfigLoaded.value) return 'error'
    return isActiveConfigLoaded.value ? 'ready' : 'loading'
  })

  // The desktop bridge exposes sing-box through its Clash-compatible API, so
  // the injected backend is typed as `clash`. The explicit field is the
  // reliable marker for a host-provided, read-only TUN state.
  const hostTunEnabled = computed(() =>
    typeof activeBackend.value?.readOnlyTunEnabled === 'boolean'
      ? activeBackend.value.readOnlyTunEnabled
      : undefined,
  )

  const hasWritableApiTun = computed(
    () =>
      isActiveConfigLoaded.value && !!configs.value?.tun && !activeBackend.value?.disableTunMode,
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
    if (retryTimer || !backendSessionReady.value || isActiveConfigLoaded.value) return
    retryTimer = window.setTimeout(() => {
      retryTimer = undefined
      void ensureConfigLoaded()
    }, 800)
  }

  const scheduleReadyRefresh = () => {
    const backendUuid = activeBackendUuid.value
    if (!backendUuid || !backendSessionReady.value) return
    const requestKey = getConfigRequestKey(backendUuid)
    if (readyRefreshTimer || readyRefreshedRequestKeys.has(requestKey)) return

    readyRefreshedRequestKeys.add(requestKey)
    readyRefreshTimer = window.setTimeout(() => {
      readyRefreshTimer = undefined
      if (getConfigRequestKey(activeBackendUuid.value) !== requestKey) return
      void fetchConfigs().catch((error) => {
        if (getConfigRequestKey(activeBackendUuid.value) === requestKey) {
          configError.value = error
        }
      })
    }, 800)
  }

  const ensureConfigLoaded = async () => {
    if (!backendSessionReady.value || isActiveConfigLoaded.value) return
    const requestedBackendUuid = activeBackendUuid.value
    const requestKey = getConfigRequestKey(requestedBackendUuid)
    if (loadingRequestKey === requestKey) return

    loadingRequestKey = requestKey
    isConfigLoading.value = true
    configError.value = null
    try {
      await fetchConfigs()
    } catch (error) {
      if (getConfigRequestKey(activeBackendUuid.value) === requestKey) {
        configError.value = error
      }
    } finally {
      if (loadingRequestKey !== requestKey) return

      loadingRequestKey = ''
      isConfigLoading.value = false
      if (activeBackend.value && !isActiveConfigLoaded.value) scheduleConfigRetry()
    }
  }

  // The desktop host replaces the injected backend in place when switching
  // cores, preserving its UUID. Reset the cached config so the new core is
  // queried even though activeBackendUuid itself did not change.
  const handleHostBackendUpdated = () => {
    if (hasHostBridge) return // Desktop sessions are coordinated by the runtime owner.
    clearRetryTimer()
    clearReadyRefreshTimer()
    resetConfigs()
    configError.value = null
    void ensureConfigLoaded()
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
    [activeBackendUuid, backendSessionGeneration, backendSessionReady],
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

  window.addEventListener(HOST_BACKEND_UPDATED_EVENT, handleHostBackendUpdated)

  onScopeDispose(() => {
    clearRetryTimer()
    clearReadyRefreshTimer()
    window.removeEventListener(HOST_BACKEND_UPDATED_EVENT, handleHostBackendUpdated)
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
