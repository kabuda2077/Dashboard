// 组装层 · Clash-compatible config 门面。
import { activeUuid } from '@/store/setup'
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

export const fetchConfigs = async () => {
  const requestedGeneration = configsGeneration
  const backendUuid = activeUuid.value
  const result = await (await load()).fetchConfigs()

  if (requestedGeneration !== configsGeneration || backendUuid !== activeUuid.value) {
    return result
  }

  configs.value = result
  configsLoaded.value = true
  configsLoadedBackendUuid.value = backendUuid
  return result
}

export const updateConfigs = async (cfg: Record<string, string | boolean | object | number>) => {
  await (await load()).updateConfigs(cfg)
  await fetchConfigs()
}

// 配置 / 缓存 / DNS 维护动作(Clash 专属),经 config 域门面暴露给 view。
export {
  flushDNSCacheAPI,
  flushFakeIPAPI,
  reloadConfigsAPI,
  updateConfigsAPI,
  updateGeoDataAPI,
} from '@/api/clash'
