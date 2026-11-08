import { expect, it, vi } from 'vitest'
const { getStorage, confirm, applySettings } = vi.hoisted(() => ({ getStorage: vi.fn(), confirm: vi.fn(), applySettings: vi.fn() }))
vi.mock('@/assembly/storage', () => ({ getStorageAPI: getStorage }))
vi.mock('@/helper/confirm-dialog', () => ({ showConfirmDialog: confirm }))
vi.mock('@/helper/utils', async (original) => ({ ...await original<object>(), applyDashboardSettingsToStorage: applySettings }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: { value: false } }))
vi.mock('@/assembly/backend', () => ({ probeBackend: vi.fn().mockResolvedValue({ ok: true }) }))
vi.mock('@/router', async () => ({ default: {
  currentRoute: (await import('vue')).ref({ query: { setupMode: 'manual' } }),
  push: vi.fn(),
  replace: vi.fn(),
} }))
vi.mock('@/components/common/DashboardSettings.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/general/LanguageSelect.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/backend/EditBackendModal.vue', () => ({ default: { render: () => null } }))

it('does not apply core settings after a backend switch while confirmation is open', async () => {
  const { addBackend, activeUuid } = await import('@/store/setup')
  addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '', password: '' }, { replaceExisting: true })
  getStorage.mockResolvedValue({ data: { 'config/theme': 'dark' } })
  let complete!: (value: object) => void
  confirm.mockReturnValue(new Promise((resolve) => { complete = resolve }))
  const { syncSettingsFromCore } = await import('@/helper/auto-import-settings')
  const syncing = syncSettingsFromCore({ force: true })
  await vi.waitFor(() => expect(confirm).toHaveBeenCalled())
  activeUuid.value = null
  complete({ confirmed: true, checked: false })
  expect(await syncing).toBe(false)
  expect(applySettings).not.toHaveBeenCalled()
})

it.each([false, true])('only fetches core settings after login when automatic sync is enabled (%s)', async (enabled) => {
  const { autoSyncSettings } = await import('@/helper/auto-import-settings')
  const { default: SetupPage } = await import('@/views/SetupPage.vue')
  const { default: router } = await import('@/router')
  const { createApp, nextTick } = await import('vue')
  autoSyncSettings.value = enabled
  getStorage.mockClear().mockResolvedValue({ data: {} })
  vi.mocked(router.push).mockClear()
  const root = document.createElement('div')
  document.body.append(root)
  const app = createApp(SetupPage)
  app.config.globalProperties.$t = (key: string) => key
  app.mount(root)
  try {
    await nextTick()
    ;[...root.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'submit')!.click()
    await vi.waitFor(() => expect(router.push).toHaveBeenCalled())
    expect(getStorage).toHaveBeenCalledTimes(enabled ? 1 : 0)
  } finally {
    app.unmount()
    root.remove()
    autoSyncSettings.value = false
  }
})
