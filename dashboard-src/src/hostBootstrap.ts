import { getBackendFromUrl } from '@/helper/utils'
import { addBackend } from '@/store/setup'
import type { Backend } from '@/types'

type HostState = {
  apiUrl?: string
  secret?: string
  iconCacheMap?: Record<string, string>
}

type HostMessage = {
  type?: string
  state?: HostState
}

type HostWindow = Window & {
  chrome?: {
    webview?: {
      addEventListener?: (
        type: 'message',
        listener: (event: MessageEvent<HostMessage>) => void,
      ) => void
    }
  }
  __mihomoApplyBackend?: (state: HostState) => void
  __mihomoIconCache?: Record<string, string>
}

const normalizePath = (pathname: string) => {
  const path = pathname.replace(/\/$/, '')
  return path === '' || path === '/' ? '' : path
}

const backendFromApiUrl = (apiUrl: string | undefined, secret: string | undefined) => {
  if (!apiUrl) return null

  try {
    const url = new URL(apiUrl)
    return {
      protocol: url.protocol.replace(':', ''),
      host: url.hostname,
      port: url.port || (url.protocol === 'https:' ? '443' : '80'),
      secondaryPath: normalizePath(url.pathname),
      password: secret || '',
      label: '本机内核',
      disableUpgradeCore: true,
    } satisfies Omit<Backend, 'uuid'>
  } catch {
    return null
  }
}

const applyBackend = (backend: Omit<Backend, 'uuid'> | null) => {
  if (!backend?.protocol || !backend.host || !backend.port) return
  addBackend(backend)
}

const applyIconCache = (state: HostState | undefined) => {
  ;(window as HostWindow).__mihomoIconCache = state?.iconCacheMap || {}
  window.dispatchEvent(new CustomEvent('__mihomoIconCacheUpdated'))
}

if (!(window as HostWindow).chrome?.webview) {
  applyBackend(getBackendFromUrl())
}

;(window as HostWindow).__mihomoApplyBackend = (state) => {
  applyIconCache(state)
  applyBackend(backendFromApiUrl(state.apiUrl, state.secret))
}

;(window as HostWindow).chrome?.webview?.addEventListener?.('message', (event) => {
  if (event.data?.type === 'state') {
    applyIconCache(event.data.state)
    applyBackend(backendFromApiUrl(event.data.state?.apiUrl, event.data.state?.secret))
  }
})
