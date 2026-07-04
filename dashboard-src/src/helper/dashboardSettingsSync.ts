import { hasHostBridge, hostWindow, postHostMessage } from '@/composables/hostBridge'
import { getDashboardSettingsFromStorage, isDashboardSettingKey } from '@/helper/utils'

const SAVE_DEBOUNCE_MS = 300
const RELOAD_FLUSH_DELAY_MS = 80

let installed = false
let saveTimer: ReturnType<typeof window.setTimeout> | undefined

const settingsSnapshot = () => JSON.stringify(getDashboardSettingsFromStorage())

const installStorageMethodObservers = () => {
  try {
    const storage = window.localStorage
    const originalSetItem = storage.setItem.bind(storage)
    const originalRemoveItem = storage.removeItem.bind(storage)
    const originalClear = storage.clear.bind(storage)

    storage.setItem = (key: string, value: string) => {
      originalSetItem(key, value)
      if (isDashboardSettingKey(key)) {
        scheduleDashboardSettingsSave()
      }
    }

    storage.removeItem = (key: string) => {
      originalRemoveItem(key)
      if (isDashboardSettingKey(key)) {
        scheduleDashboardSettingsSave()
      }
    }

    storage.clear = () => {
      originalClear()
      scheduleDashboardSettingsSave()
    }

    return true
  } catch {
    return false
  }
}

const installStoragePollingFallback = () => {
  let lastSnapshot = settingsSnapshot()

  window.setInterval(() => {
    const nextSnapshot = settingsSnapshot()
    if (nextSnapshot === lastSnapshot) return

    lastSnapshot = nextSnapshot
    scheduleDashboardSettingsSave()
  }, 1000)
}

export const saveDashboardSettingsToHost = async ({ beforeReload = false } = {}) => {
  if (!hasHostBridge) return

  if (saveTimer) {
    window.clearTimeout(saveTimer)
    saveTimer = undefined
  }

  postHostMessage({
    type: 'saveDashboardSettings',
    settings: getDashboardSettingsFromStorage(),
  })

  if (beforeReload) {
    await new Promise((resolve) => window.setTimeout(resolve, RELOAD_FLUSH_DELAY_MS))
  }
}

export const scheduleDashboardSettingsSave = () => {
  if (!hasHostBridge) return

  if (saveTimer) {
    window.clearTimeout(saveTimer)
  }

  saveTimer = window.setTimeout(() => {
    void saveDashboardSettingsToHost()
  }, SAVE_DEBOUNCE_MS)
}

export const installDashboardSettingsSync = () => {
  if (installed || !hasHostBridge) return
  installed = true

  if (!installStorageMethodObservers()) {
    installStoragePollingFallback()
  }

  window.addEventListener('storage', (event) => {
    if (event.storageArea === window.localStorage && isDashboardSettingKey(event.key)) {
      scheduleDashboardSettingsSave()
    }
  })
  window.addEventListener('beforeunload', () => {
    void saveDashboardSettingsToHost()
  })

  if (hostWindow.__mihomoHasDashboardSettings === false) {
    scheduleDashboardSettingsSave()
  }
}
