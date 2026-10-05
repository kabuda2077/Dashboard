import { afterEach, expect, it, vi } from 'vitest'
import { effectScope, ref } from 'vue'
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
vi.mock('@/assembly/rules', () => ({ fetchRules: calls.rules, resetRules: calls.reset }))
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
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it('one application owner rebuilds on epoch changes, not metadata updates or repeated route visits', async () => {
  vi.resetModules()
  Object.values(calls).forEach((call) => call.mockClear())
  const host = await startMockHost()
  const { startBackendRuntime } = await import('@/helper/backendRuntime')
  const route = ref('core')
  const scope = effectScope()
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
