import { createApp, nextTick, ref, type App } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({ configs: vi.fn(), proxies: vi.fn(), rules: vi.fn(), connections: vi.fn(), logs: vi.fn(), statistics: vi.fn(), stopConnections: vi.fn(), stopLogs: vi.fn(), stopStatistics: vi.fn() }))
vi.mock('@/components/sidebar/SideBar.vue', () => ({ default: { render: () => null } }))
vi.mock('@/composables/swipe', () => ({ useSwipeRouter: () => ({ swiperRef: ref() }) }))
vi.mock('@/helper', async (original) => ({ ...await original<object>(), renderRoutes: ref([]) }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: ref(false) }))
vi.mock('@/api/clash', async (original) => ({
  ...await original<object>(),
  getConfigsAPI: mocks.configs, fetchProxiesAPI: mocks.proxies, fetchProxyProviderAPI: async () => ({ data: { providers: {} } }),
  fetchRulesAPI: mocks.rules, fetchRuleProvidersAPI: async () => ({ data: { providers: {} } }),
}))
vi.mock('@/assembly/connections', async (original) => ({ ...await original<object>(), fetchConnectionsAPI: () => { mocks.connections(); return { data: ref(), close: mocks.stopConnections } } }))
vi.mock('@/assembly/overview', () => ({
  fetchMemoryAPI: () => { mocks.statistics(); return { data: ref(), close: mocks.stopStatistics } },
  fetchTrafficAPI: () => ({ data: ref(), close: vi.fn() }),
}))
vi.mock('@/assembly/logs/clash', () => ({ subscribeLogs: () => { mocks.logs(); return { close: mocks.stopLogs } } }))
vi.mock('@/router/pageLoaders', () => ({ scheduleAfterInitialPaint: () => vi.fn() }))
let app: App | undefined
let root: HTMLElement | undefined
afterEach(() => { app?.unmount(); root?.remove(); Reflect.deleteProperty(window, 'chrome') })

it('production Home clears stale runtime immediately and rebuilds once for each meaningful desktop session', async () => {
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: { postMessage: vi.fn() } } })
  mocks.configs.mockResolvedValue({ data: {} })
  mocks.proxies.mockResolvedValue({ data: { proxies: {} } })
  mocks.rules.mockResolvedValue({ data: { rules: [] } })
  const { addBackend } = await import('@/store/setup')
  addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', password: '', secondaryPath: '' }, { replaceExisting: true })
  const bridge = await import('@/composables/hostBridge')
  const state = { coreType: 'mihomo', apiUrl: 'http://localhost:9090', secret: '', processId: 1, isRunning: true }
  bridge.applyHostState(state)
  const router = createRouter({ history: createMemoryHistory(), routes: [
    { path: '/proxies', name: 'proxies', component: { render: () => null } },
    { path: '/logs', name: 'logs', component: { render: () => null } },
    { path: '/overview', name: 'overview', component: { render: () => null } },
  ] })
  await router.push('/proxies')
  const { default: Home } = await import('@/views/HomePage.vue')
  app = createApp(Home); app.use(router)
  root = document.createElement('div'); document.body.appendChild(root); app.mount(root)
  await vi.waitFor(() => expect(mocks.proxies).toHaveBeenCalledTimes(1))
  expect(mocks.connections).toHaveBeenCalledTimes(1)
  for (let index = 0; index < 100; index++) {
    bridge.applyHostState({ ...state, latestCoreVersion: `notice-${index}` })
    await nextTick()
  }
  expect(mocks.configs).toHaveBeenCalledTimes(1)
  expect(mocks.proxies).toHaveBeenCalledTimes(1)
  expect(mocks.connections).toHaveBeenCalledTimes(1)
  bridge.applyHostState({ ...state, coreType: 'sing-box' })
  await vi.waitFor(() => expect(mocks.connections).toHaveBeenCalledTimes(2))
  expect(mocks.stopConnections).toHaveBeenCalledTimes(1)
  await router.push('/logs'); await nextTick()
  expect(mocks.logs).toHaveBeenCalledTimes(1)
  await router.push('/overview'); await nextTick()
  expect(mocks.statistics).toHaveBeenCalledTimes(1)
  const connections = await import('@/store/connections')
  const overview = await import('@/store/overview')
  const logs = await import('@/assembly/logs')
  connections.activeConnectionCount.value = 8
  overview.downloadSpeed.value = 123
  logs.isPaused.value = true
  bridge.applyHostRuntimeState({ isRunning: false, processId: null })
  await nextTick()
  expect(connections.activeConnectionCount.value).toBe(0)
  expect(overview.downloadSpeed.value).toBe(0)
  expect(logs.isPaused.value).toBe(true)
  expect(mocks.connections).toHaveBeenCalledTimes(2)
  bridge.applyHostRuntimeState({ isRunning: true, processId: 9 })
  await vi.waitFor(() => expect(mocks.connections).toHaveBeenCalledTimes(3))
  expect(mocks.statistics).toHaveBeenCalledTimes(2)
})
