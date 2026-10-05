import { afterEach, expect, it, vi } from 'vitest'

const { fetchVersion } = vi.hoisted(() => ({
  fetchVersion: vi.fn(),
}))

vi.mock('@/api/clash', () => ({
  fetchClashVersion: fetchVersion,
  restartCoreAPI: vi.fn(),
  upgradeCoreAPI: vi.fn(),
}))

afterEach(() => {
  localStorage.clear()
  sessionStorage.clear()
})

it('refreshes for browser backend switches and actual credential edits', async () => {
  fetchVersion
    .mockResolvedValueOnce({ data: { version: 'core-a' } })
    .mockResolvedValueOnce({ data: { version: 'core-b' } })
    .mockResolvedValueOnce({ data: { version: 'core-b-refreshed' } })

  const setup = await import('@/store/setup')
  setup.backendList.value = [
    {
      type: 'clash',
      protocol: 'http',
      host: 'core-a.test',
      port: '9090',
      secondaryPath: '',
      password: '',
      uuid: 'core-a',
      disableUpgradeCore: true,
    },
    {
      type: 'clash',
      protocol: 'http',
      host: 'core-b.test',
      port: '9090',
      secondaryPath: '',
      password: '',
      uuid: 'core-b',
      disableUpgradeCore: true,
    },
  ]
  setup.activeUuid.value = 'core-a'

  const { version } = await import('@/assembly/version')
  await vi.waitFor(() => expect(version.value).toBe('core-a'))

  setup.activeUuid.value = 'core-b'
  await vi.waitFor(() => expect(version.value).toBe('core-b'))

  setup.backendList.value[1].password = 'changed'
  await vi.waitFor(() => expect(version.value).toBe('core-b-refreshed'))
  expect(fetchVersion).toHaveBeenCalledTimes(3)
})
