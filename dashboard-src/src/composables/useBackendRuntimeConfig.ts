import {
  configError,
  configs,
  configsLoaded,
  configsLoadedBackendUuid,
  fetchConfigs,
  isConfigLoading,
  updateConfigs,
} from '@/assembly/config'
import { hasHostBridge, hostState } from '@/composables/hostBridge'
import { TUN_STACK } from '@/constant'
import {
  backendConnectionStatus,
  backendSessionReady,
  captureBackendSession,
} from '@/helper/backendSession'
import { activeBackend } from '@/store/setup'
import { computed, shallowRef } from 'vue'

const pendingTunWrite = shallowRef<ReturnType<typeof captureBackendSession>>()
const supportedTunStacks: string[] = Object.values(TUN_STACK)
const normalizeTunStack = (stack: string) =>
  supportedTunStacks.includes(stack.toLowerCase()) ? stack.toLowerCase() : stack

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
  const isTunUpdating = computed(() => pendingTunWrite.value?.isCurrent() ?? false)
  const isDesktopSingBox = computed(() => hasHostBridge && hostState.value.coreType === 'sing-box')
  const isHostBusy = computed(
    () => hasHostBridge && (
      !!hostState.value.isCoreUpgrading || !!hostState.value.isCoreSwitching ||
      (hostState.value.operation !== undefined && hostState.value.operation !== 'idle')
    ),
  )
  const apiTunEnabled = computed(() =>
    (configStatus.value === 'ready' || (isTunUpdating.value && isActiveConfigLoaded.value)) &&
    typeof configs.value.tun?.enable === 'boolean'
      ? configs.value.tun.enable
      : undefined,
  )
  const allowLanEnabled = computed(() =>
    configStatus.value === 'ready' && typeof configs.value['allow-lan'] === 'boolean'
      ? configs.value['allow-lan']
      : undefined,
  )
  const hasWritableApiTun = computed(
    () =>
      typeof apiTunEnabled.value === 'boolean' && backendSessionReady.value &&
      !isDesktopSingBox.value && !activeBackend.value?.disableTunMode,
  )
  const isTunBusy = computed(() => isTunUpdating.value || isHostBusy.value)
  const canWriteTun = computed(() => hasWritableApiTun.value && !isTunBusy.value)
  const tunStack = computed(() => {
    const stack = configs.value.tun?.stack
    return isActiveConfigLoaded.value && typeof stack === 'string' && stack.trim()
      ? normalizeTunStack(stack)
      : ''
  })
  const canShowTunStack = computed(() => hasWritableApiTun.value && !!tunStack.value)
  const tunStackOptions = computed(() => {
    const options: { value: string; label: string; disabled?: boolean }[] =
      supportedTunStacks.map((value) => ({ value, label: value }))
    if (tunStack.value && !supportedTunStacks.includes(tunStack.value))
      options.unshift({ value: tunStack.value, label: tunStack.value, disabled: true })
    return options
  })
  const writeTun = async (enable: boolean, stack?: string) => {
    const session = captureBackendSession()
    pendingTunWrite.value = session
    try {
      // mihomo treats omitted tun.enable as false, even for a stack-only change.
      await updateConfigs({ tun: stack === undefined ? { enable } : { enable, stack } })
      if (!session.isCurrent()) return
      return (
        configStatus.value === 'ready' && configs.value.tun?.enable === enable &&
        (stack === undefined || (
          typeof configs.value.tun.stack === 'string' &&
          normalizeTunStack(configs.value.tun.stack) === stack
        ))
      )
    } finally {
      if (pendingTunWrite.value === session) pendingTunWrite.value = undefined
    }
  }
  const tunState = computed<RuntimeTunState>(() => {
    if (hasWritableApiTun.value)
      return {
        visible: true,
        writable: canWriteTun.value,
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
    tunStack,
    tunStackOptions,
    canShowTunStack,
    isTunUpdating,
    isTunBusy,
    canWriteTun,
    allowLanEnabled,
    ensureConfigLoaded: () => fetchConfigs(true),
    updateTunEnabled: async (enabled: boolean) => {
      if (canWriteTun.value) await writeTun(enabled)
    },
    updateTunStack: async (stack: string) => {
      if (
        !canWriteTun.value || !canShowTunStack.value || !supportedTunStacks.includes(stack) ||
        stack === tunStack.value
      ) return
      return writeTun(apiTunEnabled.value!, stack)
    },
    updateAllowLan: async (enabled: boolean) => {
      if (typeof allowLanEnabled.value === 'boolean') await updateConfigs({ 'allow-lan': enabled })
    },
  }
}
