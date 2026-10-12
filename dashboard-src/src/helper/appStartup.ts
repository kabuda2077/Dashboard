import { autoImportSettings, importSettingsFromUrl } from '@/helper/auto-import-settings'
import { notifyRequestError } from '@/helper/request-error'

// Startup runs once per document, not once per App mount. Manual import remains
// independent and keeps the existing confirmation / force behavior.
let started = false

export const importStartupSettings = async () => {
  if (started || !autoImportSettings.value) return
  started = true
  try {
    await importSettingsFromUrl()
  } catch (error) {
    notifyRequestError(error)
  }
}
