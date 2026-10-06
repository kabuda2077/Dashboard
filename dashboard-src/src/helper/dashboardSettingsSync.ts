import {
  addHostMessageListener,
  commandHost,
  hasHostBridge,
  postHostMessage,
} from '@/composables/hostBridge'
import { getDashboardSettingsFromStorage, isDashboardSettingKey } from '@/helper/utils'
import { restoredPreferences } from './dashboardStartup'
import { coreDrafts, isDraftDirty } from './hostDraft'
import { flushLocalPersistence } from './persistenceBarrier'
import { canPersistPreferences, DASHBOARD_SETTINGS_CHANGED } from './settingsChanges'

type Snapshot = { sequence: number; settings: Record<string, string>; signature: string }
type Waiter = { sequence: number; resolve: () => void; reject: (error: unknown) => void }
const normalize = (settings: Record<string, string>) =>
  JSON.stringify(Object.entries(settings).sort(([a], [b]) => a.localeCompare(b)))
let acknowledged: string | undefined
let sequence = 0
let active: Snapshot | undefined
let latest: Snapshot | undefined
let waiters: Waiter[] = []
let timer: ReturnType<typeof setTimeout> | undefined
let dispose: (() => void) | undefined

const drain = async () => {
  if (active || !latest) return
  const job = latest
  latest = undefined
  active = job
  let failure: unknown
  try {
    if (job.signature !== acknowledged) {
      const result = await commandHost({
        type: 'saveDashboardPreferences',
        preferences: job.settings,
      })
      if (result.status !== 'completed' || !result.saved)
        throw new Error(result.message ?? '界面设置保存失败。')
      acknowledged = job.signature
    }
  } catch (error) {
    failure = error
    const { notifyRequestError } = await import('@/helper/requestError')
    notifyRequestError(error)
  } finally {
    const completed = waiters.filter((waiter) => waiter.sequence <= job.sequence)
    waiters = waiters.filter((waiter) => waiter.sequence > job.sequence)
    active = undefined
    for (const waiter of completed) {
      if (failure) waiter.reject(failure)
      else waiter.resolve()
    }
    void drain()
  }
}

export const saveDashboardSettingsToHost = (): Promise<void> => {
  if (!hasHostBridge) return Promise.resolve()
  if (!canPersistPreferences())
    return Promise.reject(new Error('Preference storage needs recovery before it can be saved.'))
  clearTimeout(timer)
  timer = undefined
  const settings = getDashboardSettingsFromStorage()
  const signature = normalize(settings)
  if (!active && !latest && signature === acknowledged) return Promise.resolve()
  const targetSequence = active?.signature === signature && !latest ? active.sequence : ++sequence
  if (targetSequence !== active?.sequence)
    latest = { sequence: targetSequence, settings, signature }
  const completion = new Promise<void>((resolve, reject) =>
    waiters.push({ sequence: targetSequence, resolve, reject }),
  )
  void drain()
  return completion
}

export const scheduleDashboardSettingsSave = () => {
  if (!hasHostBridge) return
  clearTimeout(timer)
  timer = setTimeout(() => {
    void saveDashboardSettingsToHost().catch(() => {})
  }, 300)
}

export const installDashboardSettingsSync = () => {
  if (dispose || !hasHostBridge) return dispose
  acknowledged ??= normalize(restoredPreferences)
  const storage = (event: StorageEvent) => {
    if (
      event.storageArea === localStorage &&
      (event.key === null || isDashboardSettingKey(event.key))
    )
      scheduleDashboardSettingsSave()
  }
  const unload = () => {
    void saveDashboardSettingsToHost().catch(() => {})
  }
  let stopped = false
  const removeFlush = addHostMessageListener(({ data }) => {
    if (data.type !== 'flushPreferences' || !data.requestId) return
    const requestId = data.requestId
    const flush = async () => {
      try {
        // Preserve v2's local-persistence barrier and draft protection, while
        // also including edits made during an in-flight host save.
        do {
          await flushLocalPersistence()
          if (stopped) return
          await saveDashboardSettingsToHost()
        } while (!stopped && normalize(getDashboardSettingsFromStorage()) !== acknowledged)
        if (stopped) return
        const hasUnsavedDraft = Object.values(coreDrafts).some(
          (draft) => draft && isDraftDirty(draft),
        )
        postHostMessage({ type: 'preferencesFlushed', requestId, value: !hasUnsavedDraft })
      } catch {
        if (!stopped) postHostMessage({ type: 'preferencesFlushed', requestId, value: false })
      }
    }
    void flush()
  })
  window.addEventListener(DASHBOARD_SETTINGS_CHANGED, scheduleDashboardSettingsSave)
  window.addEventListener('storage', storage)
  window.addEventListener('beforeunload', unload)
  dispose = () => {
    stopped = true
    removeFlush()
    clearTimeout(timer)
    window.removeEventListener(DASHBOARD_SETTINGS_CHANGED, scheduleDashboardSettingsSave)
    window.removeEventListener('storage', storage)
    window.removeEventListener('beforeunload', unload)
    dispose = undefined
  }
  window.addEventListener('pagehide', dispose, { once: true })
  scheduleDashboardSettingsSave()
  return dispose
}
