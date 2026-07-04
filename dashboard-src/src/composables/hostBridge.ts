import { readonly, ref } from 'vue'

export const HOST_ICON_CACHE_UPDATED_EVENT = '__mihomoIconCacheUpdated'

export type HostState = {
  isRunning?: boolean
  processId?: number | null
  coreType?: string
  coreTitle?: string
  coreVersion?: string
  corePath?: string
  configPath?: string
  apiUrl?: string
  secret?: string
  mihomoCorePath?: string
  mihomoConfigPath?: string
  mihomoApiUrl?: string
  mihomoSecret?: string
  singBoxCorePath?: string
  singBoxConfigPath?: string
  singBoxApiUrl?: string
  singBoxSecret?: string
  setupCompleted?: boolean
  readOnlyTunEnabled?: boolean
  startCoreOnLaunch?: boolean
  minimizeToTray?: boolean
  lightweightMode?: boolean
  autostart?: boolean
  canUpgradeCore?: boolean
  isCoreUpgrading?: boolean
  isCoreSwitching?: boolean
  isWindowMaximized?: boolean
  logText?: string
  iconCacheMap?: Record<string, string>
  dashboardSettings?: Record<string, string>
}

export type HostRuntimeState = Pick<
  HostState,
  | 'isRunning'
  | 'processId'
  | 'coreTitle'
  | 'coreVersion'
  | 'canUpgradeCore'
  | 'isCoreUpgrading'
  | 'isCoreSwitching'
  | 'isWindowMaximized'
>

export type HostMessage = {
  type?: 'state' | 'runtimeState' | 'logAppend' | 'iconCacheUpdated' | 'notice' | 'windowState' | string
  state?: HostState
  runtimeState?: HostRuntimeState
  message?: string
  logText?: string
  iconCacheMap?: Record<string, string>
  isMaximized?: boolean
}

export type HostCommand =
  | { type: 'windowDrag' }
  | { type: 'windowResize'; edge: string }
  | { type: 'windowToggleMaximize' }
  | { type: 'windowMinimize' }
  | { type: 'windowClose' }
  | { type: 'requestWindowState' }
  | { type: 'requestState' }
  | { type: 'performance'; name: string; durationMs?: number }
  | { type: 'saveDashboardSettings'; settings: Record<string, string> }
  | ({ type: string } & Record<string, unknown>)

export type HostWindow = Window & {
  chrome?: {
    webview?: {
      postMessage?: (message: unknown) => void
      addEventListener?: (
        type: 'message',
        listener: (event: MessageEvent<HostMessage>) => void,
      ) => void
      removeEventListener?: (
        type: 'message',
        listener: (event: MessageEvent<HostMessage>) => void,
      ) => void
    }
  }
  __mihomoApplyBackend?: (state: HostState) => void
  __mihomoControlSetState?: (state: HostState) => void
  __mihomoControlNotice?: (message: string) => void
  __mihomoHostCoreVersion?: string
  __mihomoIconCache?: Record<string, string>
  __mihomoDashboardSettings?: Record<string, string>
  __mihomoHasDashboardSettings?: boolean
}

export const hostWindow = window as HostWindow
export const hasHostBridge = Boolean(hostWindow.chrome?.webview?.postMessage)

const hostStateRef = ref<HostState>({})
const hostWindowMaximizedRef = ref(false)
const hostIconCacheRef = ref<Record<string, string>>({})

export const hostState = readonly(hostStateRef)
export const hostWindowMaximized = readonly(hostWindowMaximizedRef)
export const hostIconCache = readonly(hostIconCacheRef)

export const postHostMessage = (message: HostCommand | Record<string, unknown>) => {
  hostWindow.chrome?.webview?.postMessage?.(message)
}

export const addHostMessageListener = (listener: (event: MessageEvent<HostMessage>) => void) => {
  hostWindow.chrome?.webview?.addEventListener?.('message', listener)
  return () => {
    hostWindow.chrome?.webview?.removeEventListener?.('message', listener)
  }
}

export const applyHostIconCache = (iconCacheMap: Record<string, string> | undefined) => {
  hostIconCacheRef.value = iconCacheMap || {}
  hostWindow.__mihomoIconCache = hostIconCacheRef.value
  window.dispatchEvent(new CustomEvent(HOST_ICON_CACHE_UPDATED_EVENT))
}

export const applyHostState = (state: HostState | undefined) => {
  hostStateRef.value = state || {}
  hostWindow.__mihomoHostCoreVersion = state?.coreVersion || ''
  if (state && 'dashboardSettings' in state) {
    hostWindow.__mihomoDashboardSettings = state.dashboardSettings || {}
    hostWindow.__mihomoHasDashboardSettings = Boolean(state.dashboardSettings)
  }
  hostWindowMaximizedRef.value = !!state?.isWindowMaximized
  applyHostIconCache(state?.iconCacheMap)
}

export const applyHostRuntimeState = (runtimeState: HostRuntimeState | undefined) => {
  if (!runtimeState) return
  hostStateRef.value = {
    ...hostStateRef.value,
    ...runtimeState,
  }
  hostWindow.__mihomoHostCoreVersion = runtimeState.coreVersion || hostWindow.__mihomoHostCoreVersion || ''
  if (typeof runtimeState.isWindowMaximized === 'boolean') {
    hostWindowMaximizedRef.value = runtimeState.isWindowMaximized
  }
}

export const applyHostMessage = (message: HostMessage | undefined) => {
  if (!message) return
  if (message.type === 'state') {
    applyHostState(message.state)
  } else if (message.type === 'runtimeState') {
    applyHostRuntimeState(message.runtimeState)
  } else if (message.type === 'iconCacheUpdated') {
    applyHostIconCache(message.iconCacheMap)
  } else if (message.type === 'windowState') {
    hostWindowMaximizedRef.value = !!message.isMaximized
  }
}
