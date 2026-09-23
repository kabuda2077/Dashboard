// Deliberately independent of hostBridge/utils: those modules can initialize stores.
type Settings = Record<string, string>
type Bridge = {
  postMessage: (message: unknown) => void
  addEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void
  removeEventListener: (type: 'message', listener: (event: MessageEvent) => void) => void
}
type StartupWindow = Window & {
  chrome?: { webview?: Bridge }
  __mihomoHasDashboardSettings?: boolean
  __mihomoDashboardSettings?: Settings
}

export const restoreDashboardSettings = async () => {
  const host = window as StartupWindow
  const bridge = host.chrome?.webview
  if (!bridge) return
  const settings = await new Promise<Settings | null>((resolve, reject) => {
    const requestId = crypto.randomUUID()
    const finish = (error?: Error, snapshot: Settings | null = null) => {
      clearTimeout(timer)
      bridge.removeEventListener('message', receive)
      if (error) reject(error)
      else resolve(snapshot)
    }
    const receive = (event: MessageEvent) => {
      const data = event.data
      if (data?.type !== 'dashboardSettingsSnapshot' || data.requestId !== requestId) return
      const snapshot: unknown = data.settings
      if (snapshot !== null && (typeof snapshot !== 'object' || Array.isArray(snapshot)
        || !Object.entries(snapshot).every(([key, value]) => key.startsWith('config/') && typeof value === 'string'))) {
        finish(new Error('宿主返回了无效的界面设置。'))
        return
      }
      finish(undefined, snapshot as Settings | null)
    }
    const timer = setTimeout(() => finish(new Error('读取界面设置超时。')), 10000)
    bridge.addEventListener('message', receive)
    try { bridge.postMessage({ type: 'requestDashboardSettings', requestId }) }
    catch (error) { finish(error instanceof Error ? error : new Error(String(error))) }
  })
  // null means legacy local preferences should be retained; {} explicitly clears config/*.
  if (settings !== null) {
    for (let index = localStorage.length - 1; index >= 0; index--) {
      const key = localStorage.key(index)
      if (key?.startsWith('config/') && !Object.hasOwn(settings, key)) localStorage.removeItem(key)
    }
    for (const [key, value] of Object.entries(settings)) localStorage.setItem(key, value)
  }
  host.__mihomoHasDashboardSettings = settings !== null
  host.__mihomoDashboardSettings = settings ?? {}
}

export const startDashboard = async (loadApplication: () => Promise<unknown>) => {
  try {
    await restoreDashboardSettings()
    await loadApplication()
  } catch {
    const container = document.getElementById('app')
    if (!container) return
    const message = document.createElement('p')
    message.textContent = '无法加载桌面界面或恢复设置。请重试；若仍失败，请重新打开窗口。'
    const retry = document.createElement('button')
    retry.textContent = '重试'
    retry.onclick = () => { retry.disabled = true; void startDashboard(loadApplication) }
    container.replaceChildren(message, retry)
  }
}
