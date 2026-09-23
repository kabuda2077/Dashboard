import { expect, it, vi } from 'vitest'
const { getStorage, confirm, applySettings } = vi.hoisted(() => ({ getStorage: vi.fn(), confirm: vi.fn(), applySettings: vi.fn() }))
vi.mock('@/assembly/storage', () => ({ getStorageAPI: getStorage }))
vi.mock('@/helper/confirmDialog', () => ({ showConfirmDialog: confirm }))
vi.mock('@/helper/utils', async (original) => ({ ...await original<object>(), applyDashboardSettingsToStorage: applySettings }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: { value: false } }))

it('does not apply core settings after a backend switch while confirmation is open', async () => {
  const { addBackend, activeUuid } = await import('@/store/setup')
  addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '', password: '' }, { replaceExisting: true })
  getStorage.mockResolvedValue({ data: { 'config/theme': 'dark' } })
  let complete!: (value: object) => void
  confirm.mockReturnValue(new Promise((resolve) => { complete = resolve }))
  const { syncSettingsFromCore } = await import('@/helper/autoImportSettings')
  const syncing = syncSettingsFromCore({ force: true })
  await vi.waitFor(() => expect(confirm).toHaveBeenCalled())
  activeUuid.value = null
  complete({ confirmed: true, checked: false })
  expect(await syncing).toBe(false)
  expect(applySettings).not.toHaveBeenCalled()
})
