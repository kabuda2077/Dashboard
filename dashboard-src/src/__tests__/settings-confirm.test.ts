import { describe, expect, it, vi } from 'vitest'

describe('settings override confirmation', () => {
  it('remembers automatic application only after an explicit checked confirmation', async () => {
    vi.resetModules()
    localStorage.clear()

    const settings = await import('@/helper/autoImportSettings')
    const dialog = await import('@/helper/confirmDialog')

    settings.skipImportSettingsConfirm.value = false
    const confirmation = settings.confirmSettingsOverride(
      ['config/default-theme'],
      'importSettingsConfirm',
    )

    expect(dialog.confirmDialogState.value?.checkboxText).toBeTruthy()
    dialog.resolveConfirmDialog(true, true)
    await expect(confirmation).resolves.toBe(true)
    expect(settings.skipImportSettingsConfirm.value).toBe(true)

    await expect(
      settings.confirmSettingsOverride(['config/default-theme'], 'importSettingsConfirm'),
    ).resolves.toBe(true)
    expect(dialog.confirmDialogState.value).toBeUndefined()
  })
})
