import { getUrlFromBackend } from '@/helper/utils'
import { postHostMessage } from '@/composables/hostBridge'
import { activeBackend } from '@/store/setup'
import type {
  Backend,
  Config,
  NodeRank,
  Proxy,
  ProxyProvider,
  Rule,
  RuleProvider,
} from '@/types'
import axios from 'axios'
import { debounce } from 'lodash'
import ReconnectingWebSocket from 'reconnectingwebsocket'
import { shallowRef } from 'vue'
import { captureBackendSession } from '@/helper/backendSession'

export const fetchClashVersion = () => axios.get<{ version: string }>('/version')

export const fetchProxiesAPI = () => {
  return axios.get<{ proxies: Record<string, Proxy> }>('/proxies')
}

export const selectProxyAPI = (proxyGroup: string, name: string) => {
  return axios.put(`/proxies/${encodeURIComponent(proxyGroup)}`, { name })
}

export const deleteFixedProxyAPI = (proxyGroup: string) => {
  return axios.delete(`/proxies/${encodeURIComponent(proxyGroup)}`)
}

export const fetchProxyLatencyAPI = (proxyName: string, url: string, timeout: number) => {
  return axios.get<{ delay: number }>(`/proxies/${encodeURIComponent(proxyName)}/delay`, {
    params: {
      url,
      timeout,
    },
  })
}

// Provider nodes can be absent from the global /proxies map, or collide with
// same-name nodes from other providers. When we know the provider, use the
// provider-scoped healthcheck endpoint for a single node.
export const fetchProxyProviderLatencyAPI = (
  providerName: string,
  proxyName: string,
  url: string,
  timeout: number,
) => {
  return axios.get<{ delay: number }>(
    `/providers/proxies/${encodeURIComponent(providerName)}/${encodeURIComponent(proxyName)}/healthcheck`,
    {
      params: {
        url,
        timeout,
      },
    },
  )
}

export const fetchProxyGroupLatencyAPI = (proxyName: string, url: string, timeout: number) => {
  return axios.get<Record<string, number>>(`/group/${encodeURIComponent(proxyName)}/delay`, {
    params: {
      url,
      timeout,
    },
  })
}

export const fetchSmartWeightsAPI = () => {
  return axios.get<{
    message: string
    weights: Record<string, NodeRank[]>
  }>(`/group/weights`, {
    // Smart cores predating the aggregate endpoint report it as not found.
    // Keep every other transport/HTTP failure on Axios's rejection path.
    validateStatus: (status) => status === 404 || (status >= 200 && status < 300),
  })
}

// deprecated
export const fetchSmartGroupWeightsAPI = (proxyName: string) => {
  return axios.get<{
    message: string
    weights: NodeRank[]
  }>(`/group/${encodeURIComponent(proxyName)}/weights`)
}

export const flushSmartGroupWeightsAPI = () => {
  return axios.post(`/cache/smart/flush`)
}

export const fetchProxyProviderAPI = () => {
  return axios.get<{ providers: Record<string, ProxyProvider> }>('/providers/proxies')
}

export const updateProxyProviderAPI = (name: string) => {
  return axios.put(`/providers/proxies/${encodeURIComponent(name)}`)
}

export const proxyProviderHealthCheckAPI = (name: string) => {
  return axios.get<Record<string, number>>(
    `/providers/proxies/${encodeURIComponent(name)}/healthcheck`,
    {
      timeout: 15000,
    },
  )
}

export const fetchRulesAPI = () => {
  return axios.get<{ rules: Rule[] }>('/rules')
}

export const toggleRuleDisabledAPI = (data: Record<number, boolean>) => {
  return axios.patch(`/rules/disable`, data)
}

// reFind-compatible rules expose stable UUIDs at PUT /rules/{uuid}.
export const toggleRuleDisabledRefindAPI = (uuid: string) => {
  return axios.put(`/rules/${encodeURIComponent(uuid)}`)
}

// Compatibility alias for existing desktop callers; this is not sing-box native API.
export const toggleRuleDisabledSingBoxAPI = toggleRuleDisabledRefindAPI

export const fetchRuleProvidersAPI = () => {
  return axios.get<{ providers: Record<string, RuleProvider> }>('/providers/rules')
}

export const updateRuleProviderAPI = (name: string) => {
  return axios.put(`/providers/rules/${encodeURIComponent(name)}`)
}

export const blockConnectionByIdAPI = (id: string) => {
  return axios.delete(`/connections/smart/${id}`)
}

export const disconnectClashByIdAPI = (id: string) => {
  return axios.delete(`/connections/${id}`)
}

export const disconnectAllClashAPI = () => {
  return axios.delete('/connections')
}

export const getConfigsAPI = () => {
  return axios.get<Config>('/configs')
}

export const patchConfigsAPI = (configs: Record<string, string | boolean | object | number>) => {
  return axios.patch('/configs', configs)
}

export const flushFakeIPAPI = () => {
  return axios.post('/cache/fakeip/flush')
}

export const flushDNSCacheAPI = () => {
  return axios.post('/cache/dns/flush')
}

export const reloadConfigsAPI = () => {
  return axios.put('/configs?reload=true', { path: '', payload: '' })
}

export const updateGeoDataAPI = () => {
  return axios.post('/configs/geo')
}


export const upgradeCoreAPI = (type: 'release' | 'alpha' | 'auto') => {
  const url = type === 'auto' ? '/upgrade' : `/upgrade?channel=${type}`

  return axios.post(url)
}

export const restartCoreAPI = () => {
  return axios.post('/restart')
}

export const getStorageAPI = () => {
  return axios.get<Record<string, unknown>>(`/storage/zashboard`)
}

export const setStorageAPI = (value: Record<string, string>) => {
  return axios.put(`/storage/zashboard`, value)
}

export const deleteStorageAPI = () => {
  return axios.delete(`/storage/zashboard`)
}

export const createClashWebSocket = <T>(url: string, searchParams?: Record<string, string>) => {
  const startedAt = performance.now()
  let firstMessage = true
  let closed = false
  const session = captureBackendSession()
  const backend = activeBackend.value!
  const resurl = new URL(`${getUrlFromBackend(backend).replace('http', 'ws')}/${url}`)

  resurl.searchParams.append('token', backend.password || '')

  if (searchParams) {
    Object.entries(searchParams).forEach(([key, value]) => {
      resurl.searchParams.append(key, value)
    })
  }

  const data = shallowRef<T>()
  const websocket = new ReconnectingWebSocket(resurl.toString())

  const messageHandler = ({ data: message }: { data: string }) => {
    if (closed || !session.isCurrent()) return
    if (firstMessage) {
      firstMessage = false
      postHostMessage({
        type: 'performance',
        name: `ws:${url}:firstMessage`,
        durationMs: Math.round(performance.now() - startedAt),
      })
    }
    data.value = JSON.parse(message)
  }

  const delayedMessage = debounce(messageHandler, 100)
  websocket.onmessage = url === 'logs' ? messageHandler : delayedMessage
  const close = () => {
    closed = true
    delayedMessage.cancel()
    websocket.onmessage = () => {}
    websocket.close()
  }

  return {
    data,
    close,
  }
}

export const probeClashChannel = async (backend: Backend, timeout: number) => {
  const controller = new AbortController()
  const timeoutId = setTimeout(() => controller.abort(), timeout)
  try {
    const res = await fetch(`${getUrlFromBackend(backend)}/version`, {
      method: 'GET',
      headers: {
        Authorization: `Bearer ${backend.password}`,
      },
      signal: controller.signal,
    })
    return res.ok
  } catch {
    return false
  } finally {
    clearTimeout(timeoutId)
  }
}
