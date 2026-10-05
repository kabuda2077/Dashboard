import { fetchProxiesAPI, fetchProxyLatencyAPI, selectProxyAPI } from '@/api/clash'
import { proxyMap, resetProxies } from '@/assembly/proxies'
import { fetchProxies, handlerProxySelect, proxyGroupLatencyTest } from '@/assembly/proxies/clash'
import { beforeEach, expect, it, vi } from 'vitest'
const epoch = vi.hoisted(() => ({ value: 1 }))
vi.mock('@/helper/backendSession', () => ({
  captureBackendSession: () => {
    const captured = epoch.value
    return { isCurrent: () => captured === epoch.value }
  },
}))
vi.mock('@/api/clash', () => ({
  deleteFixedProxyAPI: vi.fn(),
  fetchProxiesAPI: vi.fn(async () => ({
    data: {
      proxies: {
        G: { name: 'G', type: 'Selector', all: ['new'], now: 'new' },
        new: { name: 'new', type: 'Shadowsocks', history: [] },
      },
    },
  })),
  fetchProxyGroupLatencyAPI: vi.fn(),
  fetchProxyLatencyAPI: vi.fn(),
  fetchProxyProviderAPI: vi.fn(async () => ({ data: { providers: {} } })),
  fetchProxyProviderLatencyAPI: vi.fn(),
  selectProxyAPI: vi.fn(),
}))
vi.mock('@/assembly/connections', () => ({ disconnectByIdAPI: vi.fn() }))
vi.mock('@/assembly/version', async () => ({ isSingBoxCore: (await import('vue')).ref(false) }))
vi.mock('@/helper', () => ({
  getConnectionChains: () => [],
  isProxyGroup: (name: string) => name === 'G',
}))
vi.mock('@/helper/notification', () => ({ showNotification: vi.fn() }))
vi.mock('@/helper/requestError', () => ({
  notifyRequestErrorForSession: vi.fn(),
  runManualRequest: vi.fn(),
}))
vi.mock('@/i18n', () => ({ i18n: { global: { t: (key: string) => key } } }))
vi.mock('@/store/connections', async () => ({ activeConnections: (await import('vue')).ref([]) }))
vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  const { SPEEDTEST_MODE } = await import('@/constant')
  return {
    automaticDisconnection: ref(false), independentLatencyTest: ref(false), IPv6test: ref(false),
    speedtestMode: ref(SPEEDTEST_MODE.DASHBOARD), speedtestTimeout: ref(1000),
    speedtestUrl: ref('https://fixture.example/204'), groupTestUrls: ref([]), iconReflectList: ref([]),
  }
})
beforeEach(() => {
  resetProxies()
  vi.mocked(selectProxyAPI).mockClear()
})
it('selection rereads the group replaced by a refresh', async () => {
  proxyMap.value = {
    G: { name: 'G', type: 'Selector', all: ['a', 'new'], now: 'a' },
    a: { name: 'a', type: 'Shadowsocks', history: [] },
  } as never
  await handlerProxySelect('G', 'a')
  expect(selectProxyAPI).toHaveBeenCalledWith('G', 'a')
})
it('same-epoch reset invalidates a pending proxy snapshot', async () => {
  let finish!: (value: unknown) => void
  vi.mocked(fetchProxiesAPI).mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }) as never,
  )
  const pending = fetchProxies()
  resetProxies()
  finish({ data: { proxies: { late: { name: 'late', type: 'Shadowsocks', history: [] } } } })
  await pending
  expect(proxyMap.value).toEqual({})
})
it.each(['epoch', 'reset'])(
  'new latency work does not wait behind obsolete requests (%s)',
  async (change) => {
    proxyMap.value = Object.fromEntries([
      ['G', { name: 'G', type: 'Selector', all: ['a', 'b', 'c', 'd', 'e'], now: 'a' }],
      ...['a', 'b', 'c', 'd', 'e'].map((name) => [
        name,
        { name, type: 'Shadowsocks', history: [] },
      ]),
    ]) as never
    const release: Array<() => void> = []
    const calls: string[] = []
    vi.mocked(fetchProxyLatencyAPI).mockImplementation((name) => {
      calls.push(name)
      return name === 'new'
        ? Promise.resolve({ data: { delay: 1 } } as never)
        : new Promise((resolve) => release.push(() => resolve({ data: { delay: 1 } } as never)))
    })
    const old = proxyGroupLatencyTest('G')
    await vi.waitFor(() => expect(calls).toHaveLength(5))
    if (change === 'epoch') epoch.value++
    else resetProxies()
    proxyMap.value = {
      G: { name: 'G', type: 'Selector', all: ['new'], now: 'new' },
      new: { name: 'new', type: 'Shadowsocks', history: [] },
    } as never
    const fresh = proxyGroupLatencyTest('G')
    try {
      await vi.waitFor(() => expect(calls).toContain('new'))
    } finally {
      release.forEach((done) => done())
      await Promise.all([old, fresh])
    }
  },
)
