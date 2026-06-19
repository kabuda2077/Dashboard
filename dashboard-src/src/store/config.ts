import { getConfigsAPI, patchConfigsAPI } from '@/api'
import { HOST_BACKEND_UPDATED_EVENT } from '@/constant/hostEvents'
import type { Config } from '@/types'
import { ref } from 'vue'

export const configs = ref<Config>({
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
})
export const fetchConfigs = async () => {
  configs.value = (await getConfigsAPI()).data
}

export const resetConfigs = () => {
  configs.value = {
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
}

export const refreshConfigs = async (retries = 1, delay = 500) => {
  for (let attempt = 0; attempt < retries; attempt++) {
    try {
      await fetchConfigs()
      return
    } catch {
      if (attempt === retries - 1) {
        return
      }

      await new Promise((resolve) => window.setTimeout(resolve, delay))
    }
  }
}

export const updateConfigs = async (cfg: Record<string, string | boolean | object | number>) => {
  await patchConfigsAPI(cfg)
  fetchConfigs()
}

window.addEventListener(HOST_BACKEND_UPDATED_EVENT, () => {
  resetConfigs()
  refreshConfigs(10, 600)
})
