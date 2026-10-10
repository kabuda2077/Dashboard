import { afterEach, expect, it, vi } from 'vitest'
import { createApp, effectScope, h, nextTick, ref, type App } from 'vue'
import { startMockHost } from './hostFixture'
const calls = vi.hoisted(() => ({
  config: vi.fn(),
  configStop: vi.fn(),
  reset: vi.fn(),
  connections: vi.fn(),
  stop: vi.fn(),
  proxies: vi.fn(),
  rules: vi.fn(),
  logs: vi.fn(),
  statistics: vi.fn(),
}))
vi.mock('@/assembly/config', () => ({
  resetConfigs: calls.reset,
  startConfigRuntime: calls.config,
  stopConfigRuntime: calls.configStop,
}))
vi.mock('@/assembly/proxies', () => ({ fetchProxies: calls.proxies, resetProxies: calls.reset }))
vi.mock('@/assembly/rules', async () => {
  const { ref } = await import('vue')
  return {
    fetchRules: calls.rules, resetRules: calls.reset,
    rules: ref([]), renderRules: ref([]), renderRulesProvider: ref([]), rulesTabShow: ref('rules'),
  }
})
vi.mock('@/components/controls/RulesCtrl', () => ({ default: { render: () => null } }))
vi.mock('@/components/rules/RuleCard.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/rules/RuleProvider.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/rules/RulesTable.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/VirtualScroller.vue', () => ({ default: { render: () => null } }))
vi.mock('@/composables/paddingViews', async () => ({
  usePaddingForViews: () => ({ padding: ref({}) }),
}))
vi.mock('@/assembly/logs', () => ({
  initLogs: calls.logs,
  resetLogs: calls.reset,
  stopLogs: calls.stop,
}))
vi.mock('@/store/connections', () => ({
  initConnections: calls.connections,
  resetConnections: calls.reset,
  stopConnections: calls.stop,
}))
vi.mock('@/store/overview', () => ({
  initSatistic: calls.statistics,
  resetStatistics: calls.reset,
  stopSatistic: calls.stop,
}))
vi.mock('@/store/smart', () => ({ smartOrderMap: { value: {} }, smartWeightsMap: { value: {} } }))
const scopes: ReturnType<typeof effectScope>[] = []
const mountedPages: { app: App; element: HTMLElement }[] = []
afterEach(() => {
  scopes.splice(0).forEach((scope) => scope.stop())
  mountedPages.splice(0).forEach(({ app, element }) => { app.unmount(); element.remove() })
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it('one application owner rebuilds on epoch changes, not metadata updates or repeated route visits', async () => {
  vi.resetModules()
  Object.values(calls).forEach((call) => call.mockReset())
  const host = await startMockHost()
  const { startBackendRuntime } = await import('@/helper/backendRuntime')
  const route = ref('core')
  const scope = effectScope()
  scopes.push(scope)
  scope.run(() => startBackendRuntime(route))
  await vi.waitFor(() => expect(calls.connections).toHaveBeenCalledOnce())
  expect(calls.config).toHaveBeenCalledOnce()
  for (let i = 0; i < 50; i++) host.runtime({ coreVersion: String(i) })
  expect(calls.config).toHaveBeenCalledOnce()
  route.value = 'logs'
  await vi.waitFor(() => expect(calls.logs).toHaveBeenCalledOnce())
  route.value = 'core'
  await Promise.resolve()
  route.value = 'logs'
  await Promise.resolve()
  expect(calls.logs).toHaveBeenCalledOnce()
  host.runtime({ runtimeEpoch: 2, processId: 2 })
  await vi.waitFor(() => expect(calls.connections).toHaveBeenCalledTimes(2))
  expect(calls.config).toHaveBeenCalledTimes(2)
  scope.stop()
  const count = calls.config.mock.calls.length
  host.runtime({ runtimeEpoch: 3 })
  expect(calls.config).toHaveBeenCalledTimes(count)
  expect(calls.configStop).toHaveBeenCalled()
})

const settle = async () => {
  await nextTick()
  for (let i = 0; i < 6; i++) await Promise.resolve()
}
const mountRuntime = async (ready = true) => {
  vi.resetModules()
  Object.values(calls).forEach((call) => call.mockReset())
  const host = await startMockHost({ isRunning: ready, apiStatus: ready ? 'ready' : 'idle' })
  const { startBackendRuntime } = await import('@/helper/backendRuntime')
  const route = ref('proxies')
  const scope = effectScope()
  scopes.push(scope)
  return {
    host,
    route,
    scope,
    start: () => scope.run(() => startBackendRuntime(route)),
  }
}

it('mounting the real rules page shares the application-owned initial load', async () => {
  const { start, route } = await mountRuntime()
  route.value = 'rules'
  const RulesPage = (await import('@/views/RulesPage.vue')).default
  const element = document.createElement('div')
  document.body.append(element)
  const app = createApp({ render: () => h(RulesPage) })
  mountedPages.push({ app, element })
  start()
  app.mount(element)
  await settle()
  expect(calls.rules).toHaveBeenCalledOnce()
})

it('loads proxies once on entry and refreshes on a later visit without restarting streams', async () => {
  const { start, route } = await mountRuntime()
  start()
  await new Promise(requestAnimationFrame)
  await settle()
  expect(calls.proxies).toHaveBeenCalledOnce()
  route.value = 'core'
  await settle()
  route.value = 'proxies'
  await settle()
  expect(calls.proxies).toHaveBeenCalledTimes(2)
  expect(calls.connections).toHaveBeenCalledOnce()
})

it('reuses an in-flight proxy load across route visits and retries after failure', async () => {
  const { start, route } = await mountRuntime()
  let fail!: (error: Error) => void
  calls.proxies.mockImplementationOnce(
    () =>
      new Promise((_, reject) => {
        fail = reject
      }),
  )
  start()
  await settle()
  route.value = 'core'
  await settle()
  route.value = 'proxies'
  await settle()
  expect(calls.proxies).toHaveBeenCalledOnce()
  fail(new Error('offline'))
  await settle()
  document.dispatchEvent(new Event('visibilitychange'))
  await settle()
  expect(calls.proxies).toHaveBeenCalledTimes(2)
})

it('does not load while stopped and an obsolete request cannot block the new session', async () => {
  const { start, host, route } = await mountRuntime(false)
  start()
  await new Promise(requestAnimationFrame)
  expect(calls.proxies).not.toHaveBeenCalled()
  route.value = 'core'
  await settle()
  route.value = 'proxies'
  await settle()
  expect(calls.proxies).not.toHaveBeenCalled()
  let finish!: () => void
  calls.proxies.mockImplementationOnce(
    () =>
      new Promise<void>((resolve) => {
        finish = resolve
      }),
  )
  host.runtime({ isRunning: true, apiStatus: 'ready', runtimeEpoch: 2 })
  await settle()
  host.runtime({ runtimeEpoch: 3 })
  await settle()
  expect(calls.proxies).toHaveBeenCalledTimes(2)
  finish()
  await settle()
  document.dispatchEvent(new Event('visibilitychange'))
  await settle()
  expect(calls.proxies).toHaveBeenCalledTimes(2)
})
