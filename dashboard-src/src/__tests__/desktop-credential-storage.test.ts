import { afterEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
afterEach(() => { Reflect.deleteProperty(window, 'chrome') })

it('desktop migrates persisted passwords but preserves identity and uses only acknowledged in-memory credentials', async () => {
  vi.resetModules()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: { postMessage: vi.fn() } } })
  const endpoint = { type: 'clash' as const, protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '' }
  localStorage.setItem('setup/api-list', JSON.stringify([{ ...endpoint, uuid: 'stable', password: 'legacy-secret' }]))
  localStorage.setItem('setup/active-uuid', 'stable')
  const setup = await import('@/store/setup')
  expect(localStorage.getItem('setup/api-list')).not.toContain('legacy-secret')
  expect(setup.activeBackend.value).toBeUndefined()
  const { applyHostState } = await import('@/composables/hostBridge')
  applyHostState({ apiUrl: 'http://localhost:9090', secret: 'new-secret', isRunning: true })
  setup.addBackend({ ...endpoint, password: 'new-secret' }, { replaceExisting: true })
  await nextTick()
  expect(setup.activeUuid.value).toBe('stable')
  expect(setup.activeBackend.value?.password).toBe('new-secret')
  const stored = JSON.parse(localStorage.getItem('setup/api-list')!)
  expect(stored[0].uuid).toBe('stable')
  expect(stored[0]).not.toHaveProperty('password')
  applyHostState({ apiUrl: 'http://localhost:9090', secret: '', secretDecryptionFailed: true })
  expect(setup.activeBackend.value).toBeUndefined()
})

it('browser mode retains its own credential persistence', async () => {
  vi.resetModules()
  const setup = await import('@/store/setup')
  setup.addBackend({ type: 'clash', protocol: 'http', host: 'browser', port: '9090', secondaryPath: '', password: 'browser-secret' }, { replaceExisting: true })
  await nextTick()
  expect(localStorage.getItem('setup/api-list')).toContain('browser-secret')
})
