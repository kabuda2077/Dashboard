import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { startMockHost } from './hostFixture'
beforeEach(() => {
  vi.resetModules()
  localStorage.clear()
})
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it('desktop uses host credentials and fixed profile identity without reading or migrating old browser records', async () => {
  localStorage.setItem('setup/api-list', 'old-data-is-not-read')
  const host = await startMockHost({ secret: 'memory-only' })
  const setup = await import('@/store/setup')
  expect(setup.activeBackend.value?.password).toBe('memory-only')
  expect(setup.activeUuid.value).toBe('desktop:mihomo')
  host.runtime({ secret: 'replacement', runtimeEpoch: 2 })
  await nextTick()
  expect(setup.activeBackend.value?.password).toBe('replacement')
  expect(setup.activeUuid.value).toBe('desktop:mihomo')
  expect(JSON.stringify(localStorage)).not.toContain('replacement')
  expect(localStorage.getItem('setup/api-list')).toBe('old-data-is-not-read')
  host.runtime({ coreType: 'sing-box', runtimeEpoch: 3 })
  expect(setup.activeUuid.value).toBe('desktop:sing-box')
})
it('browser mode retains its current explicit connection workflow in its own namespace', async () => {
  Reflect.deleteProperty(window, 'chrome')
  const setup = await import('@/store/setup')
  setup.addBackend({
    type: 'clash',
    protocol: 'http',
    host: 'browser',
    port: '9090',
    password: 'browser-secret',
    secondaryPath: '',
  })
  await nextTick()
  expect(localStorage.getItem('setup-v2/backends')).toContain('browser-secret')
  expect(setup.activeBackend.value?.host).toBe('browser')
})
