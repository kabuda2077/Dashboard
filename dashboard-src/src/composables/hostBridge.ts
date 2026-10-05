// Storage-neutral: this module may be loaded before any preference/backend store.
import { computed, readonly, ref } from 'vue'

export type CoreKind = 'mihomo' | 'sing-box'
export type CoreProfile = {
  revision: number
  exePath: string
  configPath: string
  apiUrl: string
  secret: string
  secretDecryptionFailed: boolean
}
export type SecretEdit = { action: 'keep' } | { action: 'replace'; value: string }
export type CoreProfileDraft = {
  exePath: string
  configPath: string
  apiUrl: string
  secret: SecretEdit
}
export type DesktopOptions = {
  startCoreOnLaunch: boolean
  minimizeToTray: boolean
  lightweightMode: boolean
  autostart: boolean
}
export type HostRuntimeState = {
  isRunning?: boolean
  processId?: number | null
  coreType?: CoreKind
  runtimeEpoch?: number
  coreTitle?: string
  coreVersion?: string
  apiStatus?: 'idle' | 'checking' | 'ready' | 'unauthorized' | 'unreachable'
  apiUrl?: string
  secret?: string
  secretDecryptionFailed?: boolean
  operation?: string
  requiresRestart?: boolean
  canUpgradeCore?: boolean
  isCoreUpgrading?: boolean
  isCoreSwitching?: boolean
  isWindowMaximized?: boolean
  readOnlyTunEnabled?: boolean | null
}
export type HostSnapshot = {
  runtime: HostRuntimeState
  profiles: Record<CoreKind, CoreProfile>
  desktopOptions: DesktopOptions
  setupCompleted?: boolean
  isAutostartUpdating?: boolean
  appVersion?: string
  latestAppVersion?: string
  isAppUpdateChecking?: boolean
  appUpdateAvailable?: boolean
  latestCoreVersion?: string
  isCoreUpdateChecking?: boolean
  coreUpdateAvailable?: boolean
  logText?: string
  droppedLogEntries?: number
  logWriteFailures?: number
  iconCacheMap?: Record<string, string>
}
export type HostState = HostRuntimeState & Partial<Omit<HostSnapshot, 'runtime'>>
export type CommandResult = {
  status: 'completed' | 'rejected' | 'failed' | 'cancelled' | 'elevationRequired'
  code: string
  message?: string
  saved?: boolean
  path?: string
}
export type HostMessage = {
  protocolVersion?: number
  type?: string
  requestId?: string
  state?: HostSnapshot
  runtimeState?: HostRuntimeState
  preferences?: Record<string, string>
  result?: CommandResult | 'available' | 'upToDate' | 'failed' | 'busy'
  manual?: boolean
  currentVersion?: string
  latestVersion?: string
  message?: string
  severity?: 'info' | 'warning' | 'error' | 'success'
  logText?: string
  iconCacheMap?: Record<string, string>
  isMaximized?: boolean
}
export type HostCommand =
  | {
      type:
        | 'bootstrap'
        | 'requestState'
        | 'refreshCoreMetadata'
        | 'checkAppUpdate'
        | 'openAppRelease'
        | 'openCoreRepository'
    }
  | {
      type:
        | 'saveProfile'
        | 'start'
        | 'restart'
        | 'switchCore'
        | 'stop'
        | 'upgradeCore'
        | 'completeSetup'
      coreType: CoreKind
      draft?: CoreProfileDraft
      expectedRevision?: number
      expectedRuntimeEpoch?: number
      confirmUnverified?: boolean
    }
  | {
      type: 'chooseCoreFile' | 'chooseConfigFile' | 'openCoreLocation' | 'openConfigLocation'
      coreType: CoreKind
    }
  | { type: 'setDesktopOption'; option: keyof DesktopOptions; value: boolean }
  | { type: 'saveDashboardPreferences'; preferences: Record<string, string> }
  | { type: 'preferencesFlushed'; requestId: string; value: boolean }
  | {
      type:
        | 'windowDrag'
        | 'windowToggleMaximize'
        | 'windowMinimize'
        | 'windowClose'
        | 'requestWindowState'
    }
  | { type: 'windowResize'; edge: string }
  | { type: 'performance'; name: string; durationMs: number }

type WebView = {
  postMessage: (message: unknown) => void
  addEventListener?: (type: 'message', listener: (event: MessageEvent<HostMessage>) => void) => void
  removeEventListener?: (
    type: 'message',
    listener: (event: MessageEvent<HostMessage>) => void,
  ) => void
}
export type HostWindow = Window & { chrome?: { webview?: WebView } }
export const hostWindow = window as HostWindow
export const hasHostBridge = Boolean(hostWindow.chrome?.webview?.postMessage)
const snapshot = ref<HostSnapshot>()
export const hostSnapshot = readonly(snapshot)
export const hostState = computed<HostState>(() =>
  snapshot.value ? { ...snapshot.value, ...snapshot.value.runtime } : {},
)
export const hostSessionGeneration = computed(() => snapshot.value?.runtime.runtimeEpoch ?? 0)
export const hostWindowMaximized = computed(() => !!snapshot.value?.runtime.isWindowMaximized)
export const hostIconCache = computed(() => snapshot.value?.iconCacheMap ?? {})

const listeners = new Set<(event: MessageEvent<HostMessage>) => void>()
const pending = new Map<
  string,
  {
    resolve: (message: HostMessage) => void
    reject: (error: Error) => void
    timer?: ReturnType<typeof setTimeout>
  }
>()
let installed = false
let closed = false
export const isPreferencesSnapshot = (value: unknown): value is Record<string, string> =>
  value !== null &&
  typeof value === 'object' &&
  !Array.isArray(value) &&
  Object.entries(value).every(
    ([key, item]) => key.startsWith('config/') && typeof item === 'string',
  )

const record = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value)
const profileValid = (value: unknown): value is CoreProfile =>
  record(value) &&
  Number.isSafeInteger(value.revision) &&
  (value.revision as number) >= 0 &&
  ['exePath', 'configPath', 'apiUrl', 'secret'].every((key) => typeof value[key] === 'string') &&
  typeof value.secretDecryptionFailed === 'boolean'
export const isHostSnapshot = (value: unknown): value is HostSnapshot =>
  record(value) &&
  record(value.runtime) &&
  ['mihomo', 'sing-box'].includes(value.runtime.coreType as string) &&
  Number.isSafeInteger(value.runtime.runtimeEpoch) &&
  (value.runtime.runtimeEpoch as number) >= 0 &&
  typeof value.runtime.isRunning === 'boolean' &&
  (value.runtime.processId === null ||
    (Number.isSafeInteger(value.runtime.processId) && (value.runtime.processId as number) > 0)) &&
  record(value.profiles) &&
  profileValid(value.profiles.mihomo) &&
  profileValid(value.profiles['sing-box']) &&
  record(value.desktopOptions) &&
  ['startCoreOnLaunch', 'minimizeToTray', 'lightweightMode', 'autostart'].every(
    (key) => typeof (value.desktopOptions as Record<string, unknown>)[key] === 'boolean',
  )

export const applyHostState = (state: HostSnapshot | undefined) => {
  if (!isHostSnapshot(state)) return
  snapshot.value = state
}
export const applyHostRuntimeState = (state: HostRuntimeState | undefined) => {
  if (state && snapshot.value)
    snapshot.value = { ...snapshot.value, runtime: { ...snapshot.value.runtime, ...state } }
}
export const applyHostIconCache = (icons: Record<string, string> | undefined) => {
  if (snapshot.value && icons) snapshot.value = { ...snapshot.value, iconCacheMap: icons }
}
export const applyHostMessage = (message: HostMessage | undefined) => {
  if (!message || message.protocolVersion !== 2) return
  if (message.state) applyHostState(message.state)
  if (message.type === 'runtimeState') applyHostRuntimeState(message.runtimeState)
  if (message.type === 'iconCacheUpdated') applyHostIconCache(message.iconCacheMap)
  if (message.type === 'windowState')
    applyHostRuntimeState({ isWindowMaximized: !!message.isMaximized })
  if (message.type === 'logAppend' && snapshot.value)
    snapshot.value = {
      ...snapshot.value,
      logText: ((snapshot.value.logText ?? '') + (message.logText ?? '')).slice(-8000),
    }
}
const receive = (event: MessageEvent<HostMessage>) => {
  const message = event.data
  if (message?.protocolVersion !== 2) return
  applyHostMessage(message)
  const request = message.requestId ? pending.get(message.requestId) : undefined
  if (request && message.requestId) {
    clearTimeout(request.timer)
    pending.delete(message.requestId)
    request.resolve(message)
  }
  for (const listener of [...listeners]) {
    try {
      listener(event)
    } catch {
      console.error('Host message subscriber failed')
    }
  }
}
const install = () => {
  if (closed || installed || !hasHostBridge) return
  hostWindow.chrome?.webview?.addEventListener?.('message', receive)
  installed = true
}
export const addHostMessageListener = (listener: (event: MessageEvent<HostMessage>) => void) => {
  install()
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}
export const requestHost = (command: HostCommand): Promise<HostMessage> => {
  if (closed || !hasHostBridge) return Promise.reject(new Error('Desktop host is unavailable'))
  install()
  const requestId = crypto.randomUUID()
  const timeout = command.type.startsWith('choose')
    ? 0
    : command.type === 'upgradeCore'
      ? 660000
      : command.type === 'setDesktopOption'
        ? 120000
        : 30000
  return new Promise((resolve, reject) => {
    const timer = timeout
      ? setTimeout(() => {
          pending.delete(requestId)
          reject(new Error('宿主操作等待超时，请检查当前状态；已提交的操作可能仍在完成。'))
        }, timeout)
      : undefined
    pending.set(requestId, { resolve, reject, timer })
    try {
      hostWindow.chrome!.webview!.postMessage({ ...command, protocolVersion: 2, requestId })
    } catch (error) {
      clearTimeout(timer)
      pending.delete(requestId)
      reject(error)
    }
  })
}
export const commandHost = async (command: HostCommand): Promise<CommandResult> => {
  const response = await requestHost(command)
  if (
    !response.result ||
    typeof response.result === 'string' ||
    !isHostSnapshot(response.state) ||
    !['completed', 'rejected', 'failed', 'cancelled', 'elevationRequired'].includes(
      response.result.status,
    ) ||
    typeof response.result.code !== 'string' ||
    (response.result.saved !== undefined && typeof response.result.saved !== 'boolean')
  )
    throw new Error('宿主返回了无效的操作结果。')
  return response.result
}
export const postHostMessage = (command: HostCommand) => {
  if (closed || !hasHostBridge) return
  if (
    command.type.startsWith('window') ||
    command.type === 'requestWindowState' ||
    command.type === 'performance' ||
    command.type === 'preferencesFlushed'
  ) {
    install()
    hostWindow.chrome!.webview!.postMessage({ ...command, protocolVersion: 2 })
    return
  }
  void commandHost(command)
    .then(async (result) => {
      if (result.status === 'failed' || result.status === 'rejected') {
        const { showHostNotice } = await import('@/helper/hostNotice')
        showHostNotice(result.message ?? result.code, 'error')
      }
    })
    .catch(async (error) => {
      const { showHostNotice } = await import('@/helper/hostNotice')
      showHostNotice(error instanceof Error ? error.message : String(error), 'error')
    })
}
export const disposeHostBridge = () => {
  closed = true
  hostWindow.chrome?.webview?.removeEventListener?.('message', receive)
  installed = false
  for (const request of pending.values()) {
    clearTimeout(request.timer)
    request.reject(new Error('Desktop document closed'))
  }
  pending.clear()
  listeners.clear()
}
window.addEventListener('pagehide', disposeHostBridge, { once: true })
