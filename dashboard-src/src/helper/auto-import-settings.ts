import { getStorageAPI } from '@/assembly/storage'
import { hasHostBridge } from '@/composables/hostBridge'
import { useDashboardStorage } from '@/composables/use-storage'
import { captureBackendSession } from '@/helper/backendSession'
import { showConfirmDialog } from '@/helper/confirm-dialog'
import { saveDashboardSettingsToHost } from '@/helper/dashboardSettingsSync'
import { showNotification } from '@/helper/notification'
import {
  applyDashboardSettingsToStorage,
  clearDashboardSettingsFromStorage,
  parseSettingsDocument,
} from '@/helper/utils'
import { i18n } from '@/i18n'
import { useStorage } from '@vueuse/core'
import { isEmpty } from 'lodash'
import { cancelBackgroundUpdates } from './backgroundUpdates'
const IMPORT_SETTINGS_URL_KEY = 'config/import-settings-url'

export const DEFAULT_SETTINGS_URL = './zashboard-settings.json'
export const importSettingsUrl = useDashboardStorage(IMPORT_SETTINGS_URL_KEY, DEFAULT_SETTINGS_URL)
export const autoImportSettings = useDashboardStorage('config/auto-import-settings', false)
export const autoSyncSettings = useDashboardStorage('config/auto-sync-settings', false)
export const skipImportSettingsConfirm = useStorage('cache/skip-import-settings-confirm', false)
export const skipSyncSettingsConfirm = useStorage('cache/skip-sync-settings-confirm', false)

const autoImportSettingsHash = useStorage('cache/auto-import-settings-hash', '')
const autoSyncSettingsHash = useStorage('cache/auto-sync-settings-hash', '')
let importController: AbortController | undefined

const cancelSettingsImport = () => {
  importController?.abort()
  importController = undefined
}
const beginSettingsImport = () => {
  cancelSettingsImport()
  importController = new AbortController()
  return importController.signal
}
const applyImportedSettings = async (settings: Record<string, unknown>, signal: AbortSignal) => {
  await cancelBackgroundUpdates()
  if (signal.aborted) return false
  applyDashboardSettingsToStorage(settings)
  await saveDashboardSettingsToHost()
  return !signal.aborted
}

const calculateSettingsHash = async (settings: Record<string, unknown>) => {
  const sortedKeys = Object.keys(settings).sort()
  const hashString = sortedKeys.map((key) => `${key}:${settings[key]}`).join('|')

  let hash = 0
  for (let i = 0; i < hashString.length; i++) {
    const char = hashString.charCodeAt(i)
    hash = (hash << 5) - hash + char
    hash = hash & hash
  }
  return Math.abs(hash).toString(16).padStart(8, '0')
}

const getOverriddenSettingKeys = (settings: Record<string, unknown>) =>
  Object.keys(settings).filter(
    (key) => key.startsWith('config/') && localStorage.getItem(key) !== settings[key],
  )

const getImportOverriddenKeys = (settings: Record<string, unknown>) =>
  Object.keys(settings).filter((key) => {
    if (key === IMPORT_SETTINGS_URL_KEY && !settings[key]) return false
    return localStorage.getItem(key) !== settings[key]
  })

export const confirmSettingsOverride = async (
  overriddenKeys: string[],
  messageKey: 'importSettingsConfirm' | 'syncSettingsConfirm',
  signal?: AbortSignal,
) => {
  if (signal?.aborted || overriddenKeys.length === 0) return false

  const isSync = messageKey === 'syncSettingsConfirm'
  const skipConfirm = isSync ? skipSyncSettingsConfirm : skipImportSettingsConfirm
  if (skipConfirm.value) return true

  const { confirmed, checked } = await showConfirmDialog(
    {
      title: i18n.global.t(isSync ? 'syncSettings' : 'importSettings'),
      message: i18n.global.t(messageKey, { keys: overriddenKeys.join('\n') }),
      checkboxText: i18n.global.t('dontAskAgainAlwaysApply'),
    },
    signal,
  )

  if (signal?.aborted) return false
  if (confirmed && checked) skipConfirm.value = true
  return confirmed
}

export const syncSettingsFromCore = async ({
  force = false,
  notify = false,
  confirm = true,
}: {
  force?: boolean
  notify?: boolean
  confirm?: boolean
} = {}) => {
  if (hasHostBridge) {
    return false
  }

  const signal = beginSettingsImport()
  const session = captureBackendSession()
  const current = () => session.isCurrent() && !signal.aborted
  try {
    const { data } = await getStorageAPI()

    if (!current() || !data || isEmpty(data)) {
      return false
    }

    data['config/auto-sync-settings'] = JSON.stringify(autoSyncSettings.value)

    const newHash = await calculateSettingsHash(data)
    if (!current()) return false

    if (!force && autoSyncSettingsHash.value === newHash) {
      return false
    }

    if (
      confirm &&
      !(await confirmSettingsOverride(
        getOverriddenSettingKeys(data),
        'syncSettingsConfirm',
        signal,
      ))
    ) {
      if (current()) autoSyncSettingsHash.value = newHash
      return false
    }
    if (!current()) return false

    await cancelBackgroundUpdates()
    if (!current()) return false
    applyDashboardSettingsToStorage(data)
    await saveDashboardSettingsToHost()
    if (!current()) return false
    autoSyncSettingsHash.value = newHash

    if (notify) {
      showNotification({
        content: 'syncSettingsSuccess',
        type: 'alert-success',
      })
    }

    location.reload()
    return true
  } catch (error) {
    if (!current()) return false
    throw error
  }
}
export const importSettingsFromUrl = async ({
  force = false,
  confirm = true,
}: {
  force?: boolean
  confirm?: boolean
} = {}) => {
  const signal = beginSettingsImport()
  try {
    const res = await fetch(importSettingsUrl.value, { signal })
    if (signal.aborted) return false
    const errorHandler = () => {
      if (signal.aborted) return
      showNotification({
        content: 'importFailed',
        params: { url: res.url },
        type: 'alert-error',
      })
    }
    if (!res.ok) {
      errorHandler()
      return false
    }
    let settings: Record<string, unknown> = {}
    try {
      settings = parseSettingsDocument(await res.json())
    } catch {
      errorHandler()
      return false
    }

    if (!settings) {
      errorHandler()
      return false
    }

    if (signal.aborted) return false
    const newHash = await calculateSettingsHash(settings)
    if (signal.aborted) return false

    if (newHash === autoImportSettingsHash.value && !force) {
      return false
    }

    if (
      confirm &&
      !(await confirmSettingsOverride(
        getImportOverriddenKeys(settings),
        'importSettingsConfirm',
        signal,
      ))
    ) {
      if (!signal.aborted) autoImportSettingsHash.value = newHash
      return false
    }
    if (signal.aborted) return false

    showNotification({
      content: 'importing',
    })
    if (settings[IMPORT_SETTINGS_URL_KEY] === '') {
      delete settings[IMPORT_SETTINGS_URL_KEY]
    }
    if (!(await applyImportedSettings(settings, signal))) return false
    autoImportSettingsHash.value = newHash
    location.reload()
    return true
  } catch (error) {
    if (signal.aborted) return false
    throw error
  }
}

export const resetDashboardSettings = async () => {
  const signal = beginSettingsImport()
  try {
    await cancelBackgroundUpdates()
    if (signal.aborted) return false
    clearDashboardSettingsFromStorage()
    await saveDashboardSettingsToHost()
    if (signal.aborted) return false
    location.reload()
    return true
  } catch (error) {
    if (signal.aborted) return false
    throw error
  }
}

export const importSettingsFromFile = async (file: File) => {
  const signal = beginSettingsImport()
  try {
    const text = await file.text()
    if (signal.aborted) return false
    const settings = parseSettingsDocument(JSON.parse(text))
    showNotification({ content: 'importing' })
    if (!(await applyImportedSettings(settings, signal))) return false
    location.reload()
    return true
  } catch (error) {
    if (signal.aborted) return false
    throw error
  }
}
