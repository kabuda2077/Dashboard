import { afterEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { startMockHost } from './hostFixture'
const fetchVersion = vi.hoisted(() => vi.fn())
vi.mock('@/api/clash', () => ({
  fetchClashVersion: fetchVersion,
  restartCoreAPI: vi.fn(),
  upgradeCoreAPI: vi.fn(),
}))
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it('desktop uses the host version and explicit core kind instead of probing or guessing from the version text', async () => {
  vi.resetModules()
  const host = await startMockHost({ coreVersion: '1.0.0' })
  const { version, isSingBoxCore } = await import('@/assembly/version')
  expect(version.value).toBe('1.0.0')
  host.runtime({ coreType: 'sing-box', runtimeEpoch: 2, coreVersion: '2.0.0' })
  await nextTick()
  expect(version.value).toBe('2.0.0')
  expect(isSingBoxCore.value).toBe(true)
  expect(fetchVersion).not.toHaveBeenCalled()
})
