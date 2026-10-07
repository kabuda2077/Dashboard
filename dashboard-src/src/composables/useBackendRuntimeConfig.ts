import {
  configError,
  configs,
  configsLoaded,
  configsLoadedBackendUuid,
  fetchConfigs,
  isConfigLoading,
  updateConfigs,
} from '@/assembly/config'
import { backendConnectionStatus } from '@/helper/backendSession'
import { activeBackend } from '@/store/setup'
import { computed } from 'vue'

export type RuntimeTunState = {
  visible: boolean
  writable: boolean
  loading: boolean
  enabled?: boolean
  source: 'api' | 'host' | 'none'
}
export const useBackendRuntimeConfig = () => {
  const isActiveConfigLoaded = computed(
    () =>
      !!activeBackend.value &&
      configsLoaded.value &&
      configsLoadedBackendUuid.value === activeBackend.value.uuid,
  )
  const configStatus = computed(() => {
    const connection = backendConnectionStatus.value
    if (connection === 'unauthorized' || connection === 'unreachable') return 'error'
    if (!activeBackend.value) return 'idle'
    if (isConfigLoading.value || connection === 'checking') return 'loading'
    if (configError.value) return 'error'
    return isActiveConfigLoaded.value ? 'ready' : 'idle'
  })
  const apiTunEnabled = computed(() =>
    configStatus.value === 'ready' && typeof configs.value.tun?.enable === 'boolean'
      ? configs.value.tun.enable
      : undefined,
  )
  const allowLanEnabled = computed(() =>
    configStatus.value === 'ready' && typeof configs.value['allow-lan'] === 'boolean'
      ? configs.value['allow-lan']
      : undefined,
  )
  const hasWritableApiTun = computed(
    () => typeof apiTunEnabled.value === 'boolean' && !activeBackend.value?.disableTunMode,
  )
  const tunState = computed<RuntimeTunState>(() => {
    if (hasWritableApiTun.value)
      return {
        visible: true,
        writable: true,
        loading: false,
        enabled: apiTunEnabled.value,
        source: 'api',
      }
    const hostValue = activeBackend.value?.readOnlyTunEnabled
    if (typeof hostValue === 'boolean')
      return {
        visible: true,
        writable: false,
        loading: false,
        enabled: hostValue,
        source: 'host',
      }
    return {
      visible: !!activeBackend.value,
      writable: false,
      loading: configStatus.value === 'loading',
      enabled: apiTunEnabled.value,
      source: typeof apiTunEnabled.value === 'boolean' ? 'api' : 'none',
    }
  })
  return {
    configs,
    configError,
    isConfigLoading,
    isActiveConfigLoaded,
    configStatus,
    tunState,
    allowLanEnabled,
    ensureConfigLoaded: () => fetchConfigs(true),
    updateTunEnabled: async (enabled: boolean) => {
      if (hasWritableApiTun.value) await updateConfigs({ tun: { enable: enabled } })
    },
    updateAllowLan: async (enabled: boolean) => {
      if (typeof allowLanEnabled.value === 'boolean') await updateConfigs({ 'allow-lan': enabled })
    },
  }
}
