import {
  configError,
  configs,
  configsLoaded,
  configsLoadedBackendUuid,
  fetchConfigs,
  isConfigLoading,
  updateConfigs,
} from '@/assembly/config'
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
  const configStatus = computed(() =>
    !activeBackend.value
      ? 'idle'
      : isActiveConfigLoaded.value
        ? 'ready'
        : configError.value
          ? 'error'
          : 'loading',
  )
  const hasWritableApiTun = computed(
    () => isActiveConfigLoaded.value && !!configs.value.tun && !activeBackend.value?.disableTunMode,
  )
  const tunState = computed<RuntimeTunState>(() => {
    if (hasWritableApiTun.value)
      return {
        visible: true,
        writable: true,
        loading: false,
        enabled: !!configs.value.tun?.enable,
        source: 'api',
      }
    const hostValue = activeBackend.value?.readOnlyTunEnabled
    if (typeof hostValue === 'boolean')
      return {
        visible: true,
        writable: false,
        loading: isConfigLoading.value,
        enabled: hostValue,
        source: 'host',
      }
    return {
      visible: !!activeBackend.value && !isActiveConfigLoaded.value,
      writable: false,
      loading: isConfigLoading.value,
      source: 'none',
    }
  })
  return {
    configs,
    configError,
    isConfigLoading,
    isActiveConfigLoaded,
    configStatus,
    tunState,
    ensureConfigLoaded: () => fetchConfigs(true),
    updateTunEnabled: async (enabled: boolean) => {
      if (hasWritableApiTun.value) await updateConfigs({ tun: { enable: enabled } })
    },
    updateAllowLan: async (enabled: boolean) => {
      if (isActiveConfigLoaded.value) await updateConfigs({ 'allow-lan': enabled })
    },
  }
}
