import { expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
const { fetchVersion } = vi.hoisted(() => ({ fetchVersion: vi.fn() }))
vi.mock('@/api/clash', () => ({ fetchClashVersion: fetchVersion, restartCoreAPI: vi.fn(), upgradeCoreAPI: vi.fn() }))

it('clearing a backend invalidates an outstanding version response before its write', async () => {
  const setup = await import('@/store/setup')
  setup.addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '', password: '', disableUpgradeCore: true }, { replaceExisting: true })
  let complete!: (value: unknown) => void
  fetchVersion.mockImplementation(() => new Promise((resolve) => { complete = resolve }))
  const { version, isCoreUpdateAvailable } = await import('@/assembly/version')
  expect(fetchVersion).toHaveBeenCalledTimes(1)
  setup.activeUuid.value = null
  complete({ data: { version: 'old-core' } })
  await nextTick()
  await nextTick()
  expect(version.value).toBe('')
  expect(isCoreUpdateAvailable.value).toBe(false)
})
