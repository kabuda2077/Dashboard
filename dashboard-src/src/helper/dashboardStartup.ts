import {
  commandHost,
  hasHostBridge,
  isHostSnapshot,
  isPreferencesSnapshot,
  requestHost,
} from '@/composables/hostBridge'
import { validatePreferenceValues } from './preferenceValues'

const historyPreferenceKeys = [
  'connection-history-aggregation-type',
  'connection-history-sorting',
] as const

export let restoredPreferences: Record<string, string> = {}

// No imports of application stores are allowed before this handshake completes.
export const restoreDashboardSettings = async () => {
  let preferences: Record<string, string> = {}
  if (hasHostBridge) {
    performance.mark('bootstrap-requested')
    const reply = await requestHost({ type: 'bootstrap' })
    if (
      reply.type !== 'bootstrap' ||
      !isHostSnapshot(reply.state) ||
      !isPreferencesSnapshot(reply.preferences)
    ) {
      throw new Error('宿主返回了无效的新版启动快照。')
    }
    preferences = { ...reply.preferences }
  } else {
    for (const key of historyPreferenceKeys) {
      const value = localStorage.getItem(`config/${key}`)
      if (value !== null) preferences[`config/${key}`] = value
    }
  }

  let migrated = false
  for (const key of historyPreferenceKeys) {
    const configKey = `config/${key}`
    const value = localStorage.getItem(`cache/${key}`)
    if (Object.hasOwn(preferences, configKey) || value === null) continue
    try {
      validatePreferenceValues({ [configKey]: value })
    } catch {
      continue // Invalid cache data must not enter the durable preference snapshot.
    }
    preferences[configKey] = value
    migrated = true
  }
  // Commit before removing the old keys or loading stores. A failed/uncertain
  // save leaves the old values intact so startup can retry without losing them.
  if (hasHostBridge && migrated) {
    const result = await commandHost({ type: 'saveDashboardPreferences', preferences })
    if (result.status !== 'completed' || !result.saved)
      throw new Error(result.message ?? '连接统计偏好保存失败。')
  }
  if (hasHostBridge) {
    for (let index = localStorage.length - 1; index >= 0; index--) {
      const key = localStorage.key(index)
      if (key?.startsWith('config/') && !Object.hasOwn(preferences, key)) localStorage.removeItem(key)
    }
  }
  for (const [key, value] of Object.entries(preferences)) localStorage.setItem(key, value)
  for (const key of historyPreferenceKeys) {
    if (Object.hasOwn(preferences, `config/${key}`)) localStorage.removeItem(`cache/${key}`)
  }
  if (hasHostBridge) {
    restoredPreferences = { ...preferences }
    performance.mark('preferences-restored')
  }
}

export const startDashboard = async (loadApplication: () => Promise<unknown>) => {
  try {
    await restoreDashboardSettings()
    await loadApplication()
  } catch {
    const container = document.getElementById('app')
    if (!container) return
    const message = document.createElement('p')
    message.textContent = '无法加载新版桌面界面或恢复设置。请重试；此版本不读取旧设置格式。'
    const retry = document.createElement('button')
    retry.textContent = '重试'
    retry.onclick = () => {
      retry.disabled = true
      void startDashboard(loadApplication)
    }
    container.replaceChildren(message, retry)
  }
}
