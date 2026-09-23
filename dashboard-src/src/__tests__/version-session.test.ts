import { nextTick } from 'vue'
import { afterEach, expect, it, vi } from 'vitest'
afterEach(() => { Reflect.deleteProperty(window, 'chrome') })
const { fetchVersion } = vi.hoisted(() => ({ fetchVersion: vi.fn().mockRejectedValue(new Error('API unavailable')) }))
vi.mock('@/api/clash', () => ({ fetchClashVersion: fetchVersion, restartCoreAPI: vi.fn(), upgradeCoreAPI: vi.fn() }))

it('accepts a late host fallback without re-probing an unchanged session', async () => {
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: { postMessage: vi.fn() } } })
  const setup = await import('@/store/setup')
  setup.addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '', password: '', disableUpgradeCore: true }, { replaceExisting: true })
  const bridge = await import('@/composables/hostBridge')
  bridge.applyHostState({ coreType: 'mihomo', apiUrl: 'http://localhost:9090', processId: 1, isRunning: true })
  const { version } = await import('@/assembly/version')
  await vi.waitFor(() => expect(fetchVersion).toHaveBeenCalledTimes(1))
  await nextTick()
  bridge.applyHostRuntimeState({ coreVersion: '1.0.0' })
  await nextTick()
  expect(version.value).toBe('1.0.0')
  for (let index = 0; index < 100; index++) {
    bridge.applyHostState({ coreType: 'mihomo', apiUrl: 'http://localhost:9090', processId: 1, isRunning: true, latestCoreVersion: `notice-${index}` })
    await nextTick()
  }
  expect(fetchVersion).toHaveBeenCalledTimes(1)
})
