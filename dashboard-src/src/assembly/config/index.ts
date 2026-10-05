import { backendSessionReady, captureBackendSession } from '@/helper/backendSession'
import { activeUuid } from '@/store/setup'
import type { Config } from '@/types'
import { ref } from 'vue'

const newDefaultConfig = (): Config => ({
  port: 0,
  'socks-port': 0,
  'redir-port': 0,
  'tproxy-port': 0,
  'mixed-port': 0,
  'allow-lan': false,
  'bind-address': '',
  mode: '',
  'mode-list': [],
  modes: [],
  'log-level': '',
  ipv6: false,
  tun: { enable: false },
})
export const defaultConfig = newDefaultConfig()
export const configs = ref<Config>(newDefaultConfig())
export const configsLoaded = ref(false)
export const configsLoadedBackendUuid = ref('')
export const configError = ref<unknown>(null)
export const isConfigLoading = ref(false)
let generation = 0
let active = false
let retry = 0
let timer: ReturnType<typeof setTimeout> | undefined
let pending:
  | {
      generation: number
      session: ReturnType<typeof captureBackendSession>
      controller: AbortController
      promise: Promise<Config | undefined>
    }
  | undefined
export const getConfigsGeneration = () => generation

const invalidate = () => {
  generation++
  pending?.controller.abort()
  pending = undefined
  clearTimeout(timer)
  timer = undefined
  isConfigLoading.value = false
}
export const stopConfigRuntime = () => {
  active = false
  invalidate()
}
export const resetConfigs = () => {
  stopConfigRuntime()
  configs.value = newDefaultConfig()
  configsLoaded.value = false
  configsLoadedBackendUuid.value = ''
  configError.value = null
  retry = 0
}
const retryable = (error: unknown) => {
  const status = (error as { response?: { status?: number } })?.response?.status
  return ![400, 401, 403, 404, 405].includes(status ?? 0)
}
const scheduleRetry = (error: unknown) => {
  if (
    !active ||
    !backendSessionReady.value ||
    configsLoaded.value ||
    !retryable(error) ||
    retry >= 4
  )
    return
  clearTimeout(timer)
  timer = setTimeout(
    () => {
      timer = undefined
      void fetchConfigs().catch(() => {})
    },
    800 * 2 ** retry++,
  )
}

export const fetchConfigs = (force = false): Promise<Config | undefined> => {
  if (force) invalidate()
  if (pending?.generation === generation && pending.session.isCurrent()) return pending.promise
  const requestedGeneration = generation
  const session = captureBackendSession()
  const backendUuid = activeUuid.value ?? ''
  const controller = new AbortController()
  const current = () =>
    session.isCurrent() && generation === requestedGeneration && !controller.signal.aborted
  isConfigLoading.value = true
  const promise = (async () => {
    try {
      const backend = await import('./clash')
      if (!current()) return
      const result = await backend.fetchConfigs(controller.signal)
      if (!current()) return
      configs.value = result
      configsLoaded.value = true
      configsLoadedBackendUuid.value = backendUuid
      configError.value = null
      retry = 0
      clearTimeout(timer)
      return result
    } catch (error) {
      if (current()) {
        configError.value = error
        scheduleRetry(error)
      }
      throw error
    } finally {
      if (pending?.controller === controller) {
        pending = undefined
        isConfigLoading.value = false
      }
    }
  })()
  pending = { generation: requestedGeneration, session, controller, promise }
  return promise
}
export const startConfigRuntime = () => {
  active = true
  retry = 0
  void fetchConfigs().catch(() => {})
}
export const updateConfigs = async (value: Record<string, string | boolean | object | number>) => {
  const session = captureBackendSession()
  // Never satisfy a post-write refresh with a GET started before the write.
  invalidate()
  const backend = await import('./clash')
  if (!session.isCurrent()) return
  await backend.updateConfigs(value)
  if (session.isCurrent()) await fetchConfigs(true)
}
export { flushDNSCacheAPI, flushFakeIPAPI, reloadConfigsAPI, updateGeoDataAPI } from '@/api/clash'
