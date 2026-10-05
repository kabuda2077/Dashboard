import {
  hasHostBridge,
  isHostSnapshot,
  isPreferencesSnapshot,
  requestHost,
} from '@/composables/hostBridge'

export let restoredPreferences: Record<string, string> = {}

// No imports of application stores are allowed before this handshake completes.
export const restoreDashboardSettings = async () => {
  if (!hasHostBridge) return
  performance.mark('bootstrap-requested')
  const reply = await requestHost({ type: 'bootstrap' })
  if (
    reply.type !== 'bootstrap' ||
    !isHostSnapshot(reply.state) ||
    !isPreferencesSnapshot(reply.preferences)
  ) {
    throw new Error('宿主返回了无效的新版启动快照。')
  }
  const preferences = reply.preferences
  for (let index = localStorage.length - 1; index >= 0; index--) {
    const key = localStorage.key(index)
    if (key?.startsWith('config/') && !Object.hasOwn(preferences, key)) localStorage.removeItem(key)
  }
  for (const [key, value] of Object.entries(preferences)) localStorage.setItem(key, value)
  restoredPreferences = { ...preferences }
  performance.mark('preferences-restored')
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
