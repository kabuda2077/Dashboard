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
it('accepts late host metadata without fetching an unchanged endpoint or altering its epoch', async () => {
  vi.resetModules()
  const host = await startMockHost({ coreVersion: '' })
  const { version } = await import('@/assembly/version')
  expect(version.value).toBe('')
  host.runtime({ coreVersion: '1.2.3' })
  await nextTick()
  expect(version.value).toBe('1.2.3')
  expect(host.bridge.hostSessionGeneration.value).toBe(1)
  expect(fetchVersion).not.toHaveBeenCalled()
})
