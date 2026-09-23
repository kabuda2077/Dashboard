import { expect, it, vi } from 'vitest'

const { fetchVersion } = vi.hoisted(() => ({
  fetchVersion: vi.fn(),
}))

vi.mock('@/api/clash', () => ({
  fetchClashVersion: fetchVersion,
  restartCoreAPI: vi.fn(),
  upgradeCoreAPI: vi.fn(),
}))

it('stays empty without an active backend and does not probe the API', async () => {
  const { isCoreUpdateAvailable, version } = await import('@/assembly/version')

  expect(version.value).toBe('')
  expect(isCoreUpdateAvailable.value).toBe(false)
  expect(fetchVersion).not.toHaveBeenCalled()
})
