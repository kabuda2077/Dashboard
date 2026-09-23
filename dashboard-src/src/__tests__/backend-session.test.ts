import axios, { AxiosError, type AxiosResponse } from 'axios'
import { beforeEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import '@/api/http'
import { addBackend, activeUuid } from '@/store/setup'
import { applyHostState, applyHostRuntimeState } from '@/composables/hostBridge'
import { captureBackendSession } from '@/helper/backendSession'
import { fetchRules } from '@/assembly/rules/clash'
import { rules } from '@/assembly/rules'
import { allProxiesLatencyTest, fetchProxies } from '@/assembly/proxies/clash'
import { independentLatencyTest, IPv6test } from '@/store/settings'
import type { Proxy } from '@/types'
import { proxyMap } from '@/assembly/proxies'

vi.mock('@/assembly/version', () => ({ isSingBoxCore: { value: false } }))

const state = { coreType: 'mihomo', apiUrl: 'http://localhost:9090', secret: 'old', processId: 1, isRunning: true }
beforeEach(() => {
  addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '', password: 'old' }, { replaceExisting: true })
  applyHostState(state)
})

it('invalidates synchronously on same-endpoint core changes and PID restarts, but not identical state', () => {
  const session = captureBackendSession()
  applyHostState({ ...state, latestCoreVersion: 'next' })
  expect(session.isCurrent()).toBe(true)
  applyHostState({ ...state, coreType: 'sing-box' })
  expect(session.isCurrent()).toBe(false)
  const restarted = captureBackendSession()
  applyHostRuntimeState({ processId: 2 })
  expect(restarted.isCurrent()).toBe(false)
})

it.each(['/proxies', '/rules', '/configs', '/cache/dns/flush'])('rejects a late successful response from %s', async (url) => {
  let complete!: () => void
  const request = axios.get(url, { adapter: (config) => new Promise<AxiosResponse>((resolve) => {
    complete = () => resolve({ config, data: { old: true }, headers: {}, status: 200, statusText: 'OK' })
  }) })
  const rejected = expect(request).rejects.toMatchObject({ code: 'ERR_CANCELED' })
  applyHostRuntimeState({ processId: 3 })
  complete()
  await rejected
})

it('does not let an old 401 clear the new active backend', async () => {
  let complete!: () => void
  const request = axios.get('/configs', { adapter: (config) => new Promise((_resolve, reject) => {
    complete = () => reject(new AxiosError('Unauthorized', '401', config, null,
      { config, data: {}, headers: {}, status: 401, statusText: 'Unauthorized' }))
  }) })
  const rejected = expect(request).rejects.toMatchObject({ code: 'ERR_CANCELED' })
  applyHostRuntimeState({ processId: 5 })
  const id = activeUuid.value
  complete()
  await rejected
  expect(activeUuid.value).toBe(id)
})

it('abandons queued latency work and old history writes after switching sessions', async () => {
  independentLatencyTest.value = false
  IPv6test.value = false
  proxyMap.value = Object.fromEntries(Array.from({ length: 8 }, (_, index) => {
    const name = `node-${index}`
    return [name, { name, type: 'Shadowsocks', history: [] } as unknown as Proxy]
  }))
  const pending: (() => void)[] = []
  const get = vi.spyOn(axios, 'get').mockImplementation(() => new Promise((resolve) => {
    pending.push(() => resolve({ data: { delay: 20 } } as AxiosResponse))
  }))
  try {
    const testing = allProxiesLatencyTest()
    await vi.waitFor(() => expect(pending).toHaveLength(5))
    applyHostRuntimeState({ processId: 11 })
    pending.forEach((complete) => complete())
    await testing
    expect(get).toHaveBeenCalledTimes(5)
    expect(Object.values(proxyMap.value).every((proxy) => proxy.history.length === 0)).toBe(true)
  } finally { get.mockRestore() }
})

it('does not write proxy/rule state when a session changes after transport resolution', async () => {
  const get = vi.spyOn(axios, 'get').mockImplementation(async (url) => ({ data:
    url === '/proxies' ? { proxies: {} } : url === '/rules' ? { rules: [] } : { providers: {} },
  }) as AxiosResponse)
  try {
    const previousProxies = proxyMap.value
    const previousRules = rules.value
    const proxies = fetchProxies(), ruleRequest = fetchRules()
    applyHostRuntimeState({ processId: 7 })
    await Promise.all([proxies, ruleRequest])
    await nextTick()
    expect(proxyMap.value).toBe(previousProxies)
    expect(rules.value).toBe(previousRules)
  } finally { get.mockRestore() }
})
