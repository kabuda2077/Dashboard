// 组装层 · Clash-compatible config 门面。
import { activeUuid } from '@/store/setup'
import { captureBackendSession } from '@/helper/backendSession'
import type { Config } from '@/types'
import { ref } from 'vue'

export const defaultConfig: Config = {
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
  tun: {
    enable: false,
  },
}

export const configs = ref<Config>({ ...defaultConfig })
export const configsLoaded = ref(false)
export const configsLoadedBackendUuid = ref('')
let configsGeneration = 0

export const getConfigsGeneration = () => configsGeneration

export const resetConfigs = () => {
  configsGeneration += 1
  configs.value = { ...defaultConfig }
  configsLoaded.value = false
  configsLoadedBackendUuid.value = ''
}

const load = () => import('./clash')

let pending: { generation: number; session: ReturnType<typeof captureBackendSession>; promise: Promise<Config | undefined> } | undefined

export const fetchConfigs = (): Promise<Config | undefined> => {
  if (pending?.generation === configsGeneration && pending.session.isCurrent()) return pending.promise
  const session = captureBackendSession()
  const promise = fetchCurrentConfigs(session)
  pending = { generation: configsGeneration, session, promise }
  void promise.finally(() => { if (pending?.promise === promise) pending = undefined }).catch(() => {})
  return promise
}

const fetchCurrentConfigs = async (session: ReturnType<typeof captureBackendSession>) => {
  const requestedGeneration = configsGeneration
  const backendUuid = activeUuid.value
  const backend = await load()
  if (!session.isCurrent()) return
  const result = await backend.fetchConfigs()

  if (!session.isCurrent() || requestedGeneration !== configsGeneration || backendUuid !== activeUuid.value) {
    return result
  }

  configs.value = result
  configsLoaded.value = true
  configsLoadedBackendUuid.value = backendUuid
  return result
}

export const updateConfigs = async (cfg: Record<string, string | boolean | object | number>) => {
  const session = captureBackendSession()
  const backend = await load()
  if (!session.isCurrent()) return
  await backend.updateConfigs(cfg)
  if (session.isCurrent()) await fetchConfigs()
}

// 配置 / 缓存 / DNS 维护动作(Clash 专属),经 config 域门面暴露给 view。
export {
  flushDNSCacheAPI,
  flushFakeIPAPI,
  reloadConfigsAPI,
  updateGeoDataAPI,
} from '@/api/clash'
