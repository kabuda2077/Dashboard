import { readonly, ref } from 'vue'

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
  secretDecryptionFailed?: boolean
  mihomoSecretDecryptionFailed?: boolean
  singBoxSecretDecryptionFailed?: boolean
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
  isAutostartUpdating?: boolean
  appVersion?: string
  latestAppVersion?: string
  isAppUpdateChecking?: boolean
  appUpdateAvailable?: boolean
  latestCoreVersion?: string
  isCoreUpdateChecking?: boolean
  coreUpdateAvailable?: boolean
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
  type?:
    'state' | 'runtimeState' | 'logAppend' | 'iconCacheUpdated' | 'notice' | 'windowState' | string
  requestId?: string
  result?: 'available' | 'upToDate' | 'failed' | 'busy'
  manual?: boolean
  currentVersion?: string
  latestVersion?: string
  success?: boolean
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
  | { type: 'checkAppUpdate' }
  | { type: 'openAppRelease' }
  | { type: 'openCoreRepository' }
  | { type: 'performance'; name: string; durationMs?: number }
  | { type: 'saveDashboardSettings'; requestId?: string; settings: Record<string, string> }
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
  __mihomoDashboardSettings?: Record<string, string>
  __mihomoHasDashboardSettings?: boolean
}

export const hostWindow = window as HostWindow
export const hasHostBridge = Boolean(hostWindow.chrome?.webview?.postMessage)

const hostSessionGenerationRef = ref(0)
export const hostSessionGeneration = readonly(hostSessionGenerationRef)
let sessionSignature = ''
const updateHostSession = () => {
  const state = hostStateRef.value
  const signature = JSON.stringify([state.coreType, state.apiUrl, state.secret, state.secretDecryptionFailed, state.processId, state.isRunning])
  if (signature !== sessionSignature) {
    sessionSignature = signature
    hostSessionGenerationRef.value++
  }
}
const hostStateRef = ref<HostState>({})
const hostWindowMaximizedRef = ref(false)
const hostIconCacheRef = ref<Record<string, string>>({})

export const hostState = readonly(hostStateRef)
export const hostWindowMaximized = readonly(hostWindowMaximizedRef)
export const hostIconCache = readonly(hostIconCacheRef)

export const postHostMessage = (message: HostCommand | Record<string, unknown>) => {
  hostWindow.chrome?.webview?.postMessage?.(message)
}

const messageListeners = new Set<(event: MessageEvent<HostMessage>) => void>()
let receiverInstalled = false
const receiveHostMessage = (event: MessageEvent<HostMessage>) => {
  applyHostMessage(event.data)
  for (const listener of [...messageListeners]) {
    try { listener(event) }
    catch { console.error('Host message subscriber failed') }
  }
}

export const addHostMessageListener = (listener: (event: MessageEvent<HostMessage>) => void) => {
  if (!receiverInstalled) {
    hostWindow.chrome?.webview?.addEventListener?.('message', receiveHostMessage)
    receiverInstalled = true
  }
  messageListeners.add(listener)
  return () => {
    messageListeners.delete(listener)
    if (messageListeners.size === 0) {
      hostWindow.chrome?.webview?.removeEventListener?.('message', receiveHostMessage)
      receiverInstalled = false
    }
  }
}

export const applyHostIconCache = (iconCacheMap: Record<string, string> | undefined) => {
  const next = iconCacheMap || {}
  const previous = hostIconCacheRef.value
  if (Object.keys(next).length === Object.keys(previous).length
    && Object.entries(next).every(([key, value]) => previous[key] === value)) return
  hostIconCacheRef.value = next
}

export const applyHostState = (state: HostState | undefined) => {
  hostStateRef.value = state || {}
  updateHostSession()
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
  updateHostSession()
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
