import {
  addHostMessageListener,
  hasHostBridge,
  hostWindow,
  postHostMessage,
} from '@/composables/hostBridge'
import { getDashboardSettingsFromStorage, isDashboardSettingKey } from '@/helper/utils'
import { DASHBOARD_SETTINGS_CHANGED } from './settingsChanges'

const SAVE_DEBOUNCE_MS = 300
const SAVE_TIMEOUT_MS = 10000
let saveQueue: Promise<void> = Promise.resolve()
let acknowledgedSnapshot: string | undefined

let disposeSync: (() => void) | undefined
let saveTimer: ReturnType<typeof window.setTimeout> | undefined

const normalizeSnapshot = (settings: Record<string, string>) => JSON.stringify(
  Object.entries(settings).sort(([left], [right]) => left.localeCompare(right)),
)

export const saveDashboardSettingsToHost = async (_options: { beforeReload?: boolean } = {}) => {
  if (!hasHostBridge) return
  if (saveTimer) {
    window.clearTimeout(saveTimer)
    saveTimer = undefined
  }
  const settings = getDashboardSettingsFromStorage()
  const snapshot = normalizeSnapshot(settings)
  const save = () => {
    // Check when this queued save starts: an earlier identical save may have
    // succeeded or failed since this snapshot was captured.
    if (snapshot === acknowledgedSnapshot) return Promise.resolve()
    return new Promise<void>((resolve, reject) => {
      const requestId = crypto.randomUUID()
      let removeListener = () => {}
      const finish = (error?: Error) => {
        window.clearTimeout(timer)
        removeListener()
        if (error) reject(error)
        else {
          acknowledgedSnapshot = snapshot
          resolve()
        }
      }
      const timer = window.setTimeout(
        () => finish(new Error('保存设置超时，请重试后再刷新。')),
        SAVE_TIMEOUT_MS,
      )
      removeListener = addHostMessageListener(({ data }) => {
        if (data?.type !== 'dashboardSettingsSaved' || data.requestId !== requestId) return
        if (data.success === true) finish()
        else finish(new Error('设置保存失败，请检查目录写入权限后重试。'))
      })
      try {
        postHostMessage({ type: 'saveDashboardSettings', requestId, settings })
      } catch (error) {
        finish(error instanceof Error ? error : new Error(String(error)))
      }
    })
  }
  const result = saveQueue.then(save)
  saveQueue = result.catch(() => undefined)
  return result.catch(async (error) => {
    const { notifyRequestError } = await import('@/helper/requestError')
    notifyRequestError(error)
    throw error
  })
}

export const scheduleDashboardSettingsSave = () => {
  if (!hasHostBridge) return

  if (saveTimer) {
    window.clearTimeout(saveTimer)
  }

  saveTimer = window.setTimeout(() => {
    void saveDashboardSettingsToHost().catch(() => undefined)
  }, SAVE_DEBOUNCE_MS)
}

export const installDashboardSettingsSync = () => {
  if (disposeSync || !hasHostBridge) return disposeSync
  if (acknowledgedSnapshot === undefined && hostWindow.__mihomoHasDashboardSettings === true) {
    acknowledgedSnapshot = normalizeSnapshot(hostWindow.__mihomoDashboardSettings ?? {})
  }
  const onStorage = (event: StorageEvent) => {
    if (
      event.storageArea === window.localStorage &&
      (event.key === null || isDashboardSettingKey(event.key))
    ) {
      scheduleDashboardSettingsSave()
    }
  }
  const onUnload = () => {
    void saveDashboardSettingsToHost().catch(() => undefined)
  }
  window.addEventListener(DASHBOARD_SETTINGS_CHANGED, scheduleDashboardSettingsSave)
  window.addEventListener('storage', onStorage)
  window.addEventListener('beforeunload', onUnload)
  let stopped = false
  disposeSync = () => {
    if (stopped) return
    stopped = true
    window.clearTimeout(saveTimer)
    saveTimer = undefined
    window.removeEventListener(DASHBOARD_SETTINGS_CHANGED, scheduleDashboardSettingsSave)
    window.removeEventListener('storage', onStorage)
    window.removeEventListener('beforeunload', onUnload)
    window.removeEventListener('pagehide', stop)
    disposeSync = undefined
  }
  const stop = disposeSync
  window.addEventListener('pagehide', stop, { once: true })
  // Reconcile defaults written before listeners were installed. Later lazy
  // imports notify through the preference adapter; no periodic scan is needed.
  scheduleDashboardSettingsSave()
  return stop
}
