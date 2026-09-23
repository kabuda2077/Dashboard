import { getStorageAPI } from '@/assembly/storage'
import { hasHostBridge } from '@/composables/hostBridge'
import { captureBackendSession } from '@/helper/backendSession'
import { showConfirmDialog } from '@/helper/confirmDialog'
import { saveDashboardSettingsToHost } from '@/helper/dashboardSettingsSync'
import { showNotification } from '@/helper/notification'
import { applyDashboardSettingsToStorage } from '@/helper/utils'
import { i18n } from '@/i18n'
import { useStorage } from '@vueuse/core'
import { useDashboardStorage } from '@/helper/storage'
import { isEmpty } from 'lodash'
const IMPORT_SETTINGS_URL_KEY = 'config/import-settings-url'

export const DEFAULT_SETTINGS_URL = './zashboard-settings.json'
export const importSettingsUrl = useDashboardStorage(IMPORT_SETTINGS_URL_KEY, DEFAULT_SETTINGS_URL)
export const autoImportSettings = useDashboardStorage('config/auto-import-settings', false)
export const autoSyncSettings = useDashboardStorage('config/auto-sync-settings', false)
export const skipImportSettingsConfirm = useStorage('cache/skip-import-settings-confirm', false)
export const skipSyncSettingsConfirm = useStorage('cache/skip-sync-settings-confirm', false)

const autoImportSettingsHash = useStorage('cache/auto-import-settings-hash', '')
const autoSyncSettingsHash = useStorage('cache/auto-sync-settings-hash', '')
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
) => {
  if (overriddenKeys.length === 0) return false

  const isSync = messageKey === 'syncSettingsConfirm'
  const skipConfirm = isSync ? skipSyncSettingsConfirm : skipImportSettingsConfirm
  if (skipConfirm.value) return true

  const { confirmed, checked } = await showConfirmDialog({
    title: i18n.global.t(isSync ? 'syncSettings' : 'importSettings'),
    message: i18n.global.t(messageKey, { keys: overriddenKeys.join('\n') }),
    checkboxText: i18n.global.t('dontAskAgainAlwaysApply'),
  })

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
  preserveAutoSyncSetting?: boolean
} = {}) => {
  if (hasHostBridge) {
    return false
  }

  const session = captureBackendSession()
  const { data } = await getStorageAPI()

  if (!session.isCurrent() || !data || isEmpty(data)) {
    return false
  }

  data['config/auto-sync-settings'] = JSON.stringify(autoSyncSettings.value)

  const newHash = await calculateSettingsHash(data)
  if (!session.isCurrent()) return false

  if (!force && autoSyncSettingsHash.value === newHash) {
    return false
  }

  if (
    confirm &&
    !(await confirmSettingsOverride(getOverriddenSettingKeys(data), 'syncSettingsConfirm'))
  ) {
    if (session.isCurrent()) autoSyncSettingsHash.value = newHash
    return false
  }
  if (!session.isCurrent()) return false

  applyDashboardSettingsToStorage(data)
  await saveDashboardSettingsToHost({ beforeReload: true })
  if (!session.isCurrent()) return false
  autoSyncSettingsHash.value = newHash

  if (notify) {
    showNotification({
      content: 'syncSettingsSuccess',
      type: 'alert-success',
    })
  }

  location.reload()
  return true
}
export const importSettingsFromUrl = async ({
  force = false,
  confirm = true,
}: {
  force?: boolean
  confirm?: boolean
} = {}) => {
  const res = await fetch(importSettingsUrl.value)
  const errorHandler = () => {
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
    settings = await res.json()
  } catch {
    errorHandler()
    return false
  }

  if (!settings) {
    errorHandler()
    return false
  }

  const newHash = await calculateSettingsHash(settings)

  if (newHash === autoImportSettingsHash.value && !force) {
    return false
  }

  if (
    confirm &&
    !(await confirmSettingsOverride(getImportOverriddenKeys(settings), 'importSettingsConfirm'))
  ) {
    autoImportSettingsHash.value = newHash
    return false
  }

  showNotification({
    content: 'importing',
  })
  if (settings[IMPORT_SETTINGS_URL_KEY] === '') {
    delete settings[IMPORT_SETTINGS_URL_KEY]
  }
  applyDashboardSettingsToStorage(settings)
  await saveDashboardSettingsToHost({ beforeReload: true })
  autoImportSettingsHash.value = newHash
  location.reload()
  return true
}
