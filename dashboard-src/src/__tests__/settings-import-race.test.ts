import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const { apply, clear, save, cancel, notify, getStorage } = vi.hoisted(() => ({
  apply: vi.fn(),
  clear: vi.fn(),
  save: vi.fn(),
  cancel: vi.fn(),
  notify: vi.fn(),
  getStorage: vi.fn(),
}))
vi.mock('@/assembly/storage', () => ({ getStorageAPI: getStorage }))
vi.mock('@/composables/hostBridge', () => ({ hasHostBridge: false }))
vi.mock('@/composables/use-storage', async () => {
  const { ref } = await import('vue')
  return { useDashboardStorage: (_key: string, value: unknown) => ref(value) }
})
vi.mock('@vueuse/core', async () => {
  const { ref } = await import('vue')
  return { useStorage: (_key: string, value: unknown) => ref(value) }
})
vi.mock('@/helper/backendSession', () => ({
  captureBackendSession: () => ({ isCurrent: () => true }),
}))
vi.mock('@/helper/dashboardSettingsSync', () => ({ saveDashboardSettingsToHost: save }))
vi.mock('@/helper/backgroundUpdates', () => ({ cancelBackgroundUpdates: cancel }))
vi.mock('@/helper/notification', () => ({ showNotification: notify }))
vi.mock('@/helper/utils', () => ({
  parseSettingsDocument: (value: unknown) => value,
  applyDashboardSettingsToStorage: apply,
  clearDashboardSettingsFromStorage: clear,
}))
vi.mock('@/i18n', () => ({ i18n: { global: { t: (key: string) => key } } }))

const deferred = <T>() => {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((done, fail) => {
    resolve = done
    reject = fail
  })
  return { promise, resolve, reject }
}
const response = (theme: string) => new Response(JSON.stringify({ 'config/theme': theme }))
let fetchMock = vi.fn<typeof fetch>()
const reload = vi.fn()

beforeEach(() => {
  vi.resetModules()
  vi.clearAllMocks()
  save.mockResolvedValue(undefined)
  cancel.mockResolvedValue(undefined)
  fetchMock = vi.fn<typeof fetch>()
  vi.stubGlobal('fetch', fetchMock)
  vi.stubGlobal('location', { reload })
})
afterEach(() => vi.unstubAllGlobals())

it('discards a late automatic response while the newer URL import is still pending', async () => {
  const old = deferred<Response>()
  const latest = deferred<Response>()
  fetchMock.mockReturnValueOnce(old.promise).mockReturnValueOnce(latest.promise)
  const settings = await import('@/helper/auto-import-settings')
  const automatic = settings.importSettingsFromUrl({ confirm: false })
  const manual = settings.importSettingsFromUrl({ force: true, confirm: false })
  expect(fetchMock.mock.calls[0][1]?.signal?.aborted).toBe(true)
  old.resolve(response('old'))
  expect(await automatic).toBe(false)
  expect(apply).not.toHaveBeenCalled()
  expect(reload).not.toHaveBeenCalled()
  latest.resolve(response('latest'))
  expect(await manual).toBe(true)
  expect(apply).toHaveBeenCalledExactlyOnceWith({ 'config/theme': 'latest' })
  expect(save).toHaveBeenCalledTimes(1)
  expect(reload).toHaveBeenCalledTimes(1)
})

it('does not reload from an older save while a newer file is being read', async () => {
  const oldSave = deferred<void>()
  const fileRead = deferred<string>()
  save.mockReturnValueOnce(oldSave.promise).mockResolvedValueOnce(undefined)
  fetchMock.mockResolvedValue(response('old'))
  const settings = await import('@/helper/auto-import-settings')
  const automatic = settings.importSettingsFromUrl({ confirm: false })
  await vi.waitFor(() => expect(save).toHaveBeenCalledTimes(1))
  const manual = settings.importSettingsFromFile({ text: () => fileRead.promise } as File)
  oldSave.resolve()
  expect(await automatic).toBe(false)
  expect(reload).not.toHaveBeenCalled()
  fileRead.resolve(JSON.stringify({ 'config/theme': 'file' }))
  expect(await manual).toBe(true)
  expect(apply).toHaveBeenLastCalledWith({ 'config/theme': 'file' })
  expect(save).toHaveBeenCalledTimes(2)
  expect(reload).toHaveBeenCalledTimes(1)
})

it('removes an obsolete confirmation without changing the skip-confirm preference', async () => {
  fetchMock.mockResolvedValueOnce(response('old')).mockResolvedValueOnce(response('latest'))
  const settings = await import('@/helper/auto-import-settings')
  const { confirmDialogState } = await import('@/helper/confirm-dialog')
  const automatic = settings.importSettingsFromUrl()
  await vi.waitFor(() => expect(confirmDialogState.value).toBeDefined())
  const manual = settings.importSettingsFromUrl({ force: true, confirm: false })
  expect(confirmDialogState.value).toBeUndefined()
  expect(await automatic).toBe(false)
  expect(await manual).toBe(true)
  expect(settings.skipImportSettingsConfirm.value).toBe(false)
  expect(apply).toHaveBeenCalledExactlyOnceWith({ 'config/theme': 'latest' })
  expect(reload).toHaveBeenCalledTimes(1)
})

it('a settings reset invalidates a pending import', async () => {
  const old = deferred<Response>()
  fetchMock.mockReturnValue(old.promise)
  const settings = await import('@/helper/auto-import-settings')
  const importing = settings.importSettingsFromUrl({ confirm: false })
  expect(await settings.resetDashboardSettings()).toBe(true)
  old.resolve(response('old'))
  expect(await importing).toBe(false)
  expect(clear).toHaveBeenCalledTimes(1)
  expect(apply).not.toHaveBeenCalled()
  expect(reload).toHaveBeenCalledTimes(1)
})

it.each(['sync', 'reset'] as const)(
  'suppresses an obsolete %s failure while a newer file is being read',
  async (kind) => {
    const old = deferred<never>()
    const fileRead = deferred<string>()
    const settings = await import('@/helper/auto-import-settings')
    let previous: Promise<boolean>
    if (kind === 'sync') {
      getStorage.mockReturnValueOnce(old.promise)
      previous = settings.syncSettingsFromCore()
    } else {
      save.mockReturnValueOnce(old.promise)
      previous = settings.resetDashboardSettings()
      await vi.waitFor(() => expect(save).toHaveBeenCalledTimes(1))
    }
    const manual = settings.importSettingsFromFile({ text: () => fileRead.promise } as File)
    old.reject(new Error('obsolete operation failed'))
    expect(await previous).toBe(false)
    expect(reload).not.toHaveBeenCalled()
    expect(notify).not.toHaveBeenCalledWith(expect.objectContaining({ type: 'alert-error' }))
    fileRead.resolve(JSON.stringify({ 'config/theme': 'latest' }))
    expect(await manual).toBe(true)
    expect(reload).toHaveBeenCalledTimes(1)
  },
)

it('reports a current save failure and leaves the page open', async () => {
  fetchMock.mockResolvedValue(response('new'))
  save.mockRejectedValue(new Error('save failed'))
  const { importSettingsFromUrl } = await import('@/helper/auto-import-settings')
  await expect(importSettingsFromUrl({ confirm: false })).rejects.toThrow('save failed')
  expect(reload).not.toHaveBeenCalled()
})
