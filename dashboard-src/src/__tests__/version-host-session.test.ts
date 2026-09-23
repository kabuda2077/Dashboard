import { afterEach, expect, it, vi } from 'vitest'

const { fetchVersion } = vi.hoisted(() => ({
  fetchVersion: vi.fn().mockRejectedValue(new Error('API unavailable')),
}))

vi.mock('@/api/clash', () => ({
  fetchClashVersion: fetchVersion,
  restartCoreAPI: vi.fn(),
  upgradeCoreAPI: vi.fn(),
}))

afterEach(() => {
  Reflect.deleteProperty(window, 'chrome')
  localStorage.clear()
  sessionStorage.clear()
})

it('re-probes when the desktop host session changes at the same endpoint', async () => {
  Object.defineProperty(window, 'chrome', {
    configurable: true,
    value: { webview: { postMessage: vi.fn() } },
  })

  const setup = await import('@/store/setup')
  setup.backendList.value = [
    {
      type: 'clash',
      protocol: 'http',
      host: 'localhost',
      port: '9090',
      secondaryPath: '',
      password: '',
      uuid: 'desktop-core',
      disableUpgradeCore: true,
    },
  ]
  setup.activeUuid.value = 'desktop-core'

  const bridge = await import('@/composables/hostBridge')
  bridge.applyHostState({
    coreType: 'mihomo',
    apiUrl: 'http://localhost:9090',
    processId: 1,
    isRunning: true,
  })

  await import('@/assembly/version')
  await vi.waitFor(() => expect(fetchVersion).toHaveBeenCalledTimes(1))

  bridge.applyHostState({
    coreType: 'mihomo',
    apiUrl: 'http://localhost:9090',
    processId: 2,
    isRunning: true,
  })

  await vi.waitFor(() => expect(fetchVersion).toHaveBeenCalledTimes(2))
})
