import { getBackendFromUrl } from '@/helper/utils'
import { addBackend } from '@/store/setup'
import { HOST_BACKEND_UPDATED_EVENT } from '@/constant/hostEvents'
import type { Backend } from '@/types'

type HostState = {
  coreType?: string
  apiUrl?: string
  secret?: string
  readOnlyTunEnabled?: boolean
  iconCacheMap?: Record<string, string>
}

type HostMessage = {
  type?: string
  state?: HostState
}

type HostWindow = Window & {
  chrome?: {
    webview?: {
      postMessage?: (message: unknown) => void
      addEventListener?: (
        type: 'message',
        listener: (event: MessageEvent<HostMessage>) => void,
      ) => void
    }
  }
  __mihomoApplyBackend?: (state: HostState) => void
  __mihomoIconCache?: Record<string, string>
}

const RESIZE_BORDER_SIZE = 8
const RESIZE_HANDLE_Z_INDEX = '2147483647'
const DRAG_REGION_HEIGHT = 48

const normalizePath = (pathname: string) => {
  const path = pathname.replace(/\/$/, '')
  return path === '' || path === '/' ? '' : path
}

const backendFromApiUrl = (
  apiUrl: string | undefined,
  secret: string | undefined,
  coreType: string | undefined,
  readOnlyTunEnabled: boolean | undefined,
) => {
  if (!apiUrl) return null

  try {
    const url = new URL(apiUrl)
    return {
      protocol: url.protocol.replace(':', ''),
      host: url.hostname,
      port: url.port || (url.protocol === 'https:' ? '443' : '80'),
      secondaryPath: normalizePath(url.pathname),
      password: secret || '',
      label: coreType === 'sing-box' ? '本机 sing-box' : '本机内核',
      disableUpgradeCore: true,
      readOnlyTunEnabled: coreType === 'sing-box' ? !!readOnlyTunEnabled : undefined,
    } satisfies Omit<Backend, 'uuid'>
  } catch {
    return null
  }
}

const applyBackend = (backend: Omit<Backend, 'uuid'> | null, replaceExisting = false) => {
  if (!backend?.protocol || !backend.host || !backend.port) return
  addBackend(backend, { replaceExisting })
}

const applyBackendFromState = (state: HostState | undefined, replaceExisting = false) => {
  const backend = backendFromApiUrl(
    state?.apiUrl,
    state?.secret,
    state?.coreType,
    state?.readOnlyTunEnabled,
  )
  applyBackend(backend, replaceExisting)
  return backend
}

let backendSignature = ''
const applyHostState = (state: HostState | undefined) => {
  applyIconCache(state)
  const backend = applyBackendFromState(state, true)
  const nextSignature = backend
    ? `${backend.protocol}://${backend.host}:${backend.port}${backend.secondaryPath}|${backend.password}|${state?.coreType ?? ''}`
    : ''

  if (nextSignature && nextSignature !== backendSignature) {
    backendSignature = nextSignature
    window.dispatchEvent(new CustomEvent(HOST_BACKEND_UPDATED_EVENT))
  }
}

const applyIconCache = (state: HostState | undefined) => {
  ;(window as HostWindow).__mihomoIconCache = state?.iconCacheMap || {}
  window.dispatchEvent(new CustomEvent('__mihomoIconCacheUpdated'))
}

const postHostMessage = (message: unknown) => {
  ;(window as HostWindow).chrome?.webview?.postMessage?.(message)
}

const getResizeEdge = (event: MouseEvent) => {
  const nearLeft = event.clientX <= RESIZE_BORDER_SIZE
  const nearRight = window.innerWidth - event.clientX <= RESIZE_BORDER_SIZE
  const nearTop = event.clientY <= RESIZE_BORDER_SIZE
  const nearBottom = window.innerHeight - event.clientY <= RESIZE_BORDER_SIZE

  if (nearTop && nearLeft) return 'topLeft'
  if (nearTop && nearRight) return 'topRight'
  if (nearBottom && nearLeft) return 'bottomLeft'
  if (nearBottom && nearRight) return 'bottomRight'
  if (nearLeft) return 'left'
  if (nearRight) return 'right'
  if (nearTop) return 'top'
  if (nearBottom) return 'bottom'
  return null
}

const resizeCursorMap: Record<string, string> = {
  topLeft: 'nwse-resize',
  bottomRight: 'nwse-resize',
  topRight: 'nesw-resize',
  bottomLeft: 'nesw-resize',
  left: 'ew-resize',
  right: 'ew-resize',
  top: 'ns-resize',
  bottom: 'ns-resize',
}

const resizeHandleStyles: Record<string, Partial<CSSStyleDeclaration>> = {
  topLeft: {
    top: '0',
    left: '0',
    width: `${RESIZE_BORDER_SIZE * 2}px`,
    height: `${RESIZE_BORDER_SIZE * 2}px`,
  },
  topRight: {
    top: '0',
    right: '0',
    width: `${RESIZE_BORDER_SIZE * 2}px`,
    height: `${RESIZE_BORDER_SIZE * 2}px`,
  },
  bottomLeft: {
    bottom: '0',
    left: '0',
    width: `${RESIZE_BORDER_SIZE * 2}px`,
    height: `${RESIZE_BORDER_SIZE * 2}px`,
  },
  bottomRight: {
    right: '0',
    bottom: '0',
    width: `${RESIZE_BORDER_SIZE * 2}px`,
    height: `${RESIZE_BORDER_SIZE * 2}px`,
  },
  left: {
    top: `${RESIZE_BORDER_SIZE}px`,
    bottom: `${RESIZE_BORDER_SIZE}px`,
    left: '0',
    width: `${RESIZE_BORDER_SIZE}px`,
  },
  right: {
    top: `${RESIZE_BORDER_SIZE}px`,
    right: '0',
    bottom: `${RESIZE_BORDER_SIZE}px`,
    width: `${RESIZE_BORDER_SIZE}px`,
  },
  top: {
    top: '0',
    right: `${RESIZE_BORDER_SIZE}px`,
    left: `${RESIZE_BORDER_SIZE}px`,
    height: `${RESIZE_BORDER_SIZE}px`,
  },
  bottom: {
    right: `${RESIZE_BORDER_SIZE}px`,
    bottom: '0',
    left: `${RESIZE_BORDER_SIZE}px`,
    height: `${RESIZE_BORDER_SIZE}px`,
  },
}

const isInteractiveElement = (target: EventTarget | null) => {
  if (!(target instanceof Element)) return false

  return Boolean(
    target.closest(
      [
        'a',
        'button',
        'input',
        'select',
        'textarea',
        'label',
        '[contenteditable="true"]',
        '[role="button"]',
        '.base-container',
        '.btn',
        '.dock',
        '.dropdown',
        '.input',
        '.join',
        '.menu',
        '.modal',
        '.select',
        '.sidebar',
      ].join(','),
    ),
  )
}

const installWindowChromeBridge = () => {
  if (!(window as HostWindow).chrome?.webview?.postMessage) return

  const startResize = (edge: string, event: MouseEvent) => {
    event.preventDefault()
    event.stopPropagation()
    postHostMessage({ type: 'windowResize', edge })
  }

  const installResizeHandles = () => {
    for (const [edge, edgeStyle] of Object.entries(resizeHandleStyles)) {
      const handle = document.createElement('div')
      handle.dataset.windowResizeEdge = edge
      Object.assign(handle.style, {
        position: 'fixed',
        zIndex: RESIZE_HANDLE_Z_INDEX,
        background: 'transparent',
        pointerEvents: 'auto',
        cursor: resizeCursorMap[edge],
        touchAction: 'none',
        userSelect: 'none',
        ...edgeStyle,
      })
      handle.addEventListener('mousedown', (event) => {
        if (event.button !== 0) return
        startResize(edge, event)
      })
      document.body.appendChild(handle)
    }
  }

  if (document.body) {
    installResizeHandles()
  } else {
    window.addEventListener('DOMContentLoaded', installResizeHandles, { once: true })
  }

  window.addEventListener(
    'mousemove',
    (event) => {
      if (event.buttons !== 0) return
      const edge = getResizeEdge(event)
      document.documentElement.style.cursor = edge ? resizeCursorMap[edge] : ''
    },
    { passive: true },
  )

  window.addEventListener('mouseleave', () => {
    document.documentElement.style.cursor = ''
  })

  window.addEventListener(
    'mousedown',
    (event) => {
      if (event.button !== 0) return

      const edge = getResizeEdge(event)
      if (edge) {
        startResize(edge, event)
        return
      }

      if (event.clientY <= DRAG_REGION_HEIGHT && !isInteractiveElement(event.target)) {
        event.preventDefault()
        postHostMessage({ type: 'windowDrag' })
      }
    },
    { capture: true },
  )

  window.addEventListener(
    'dblclick',
    (event) => {
      if (event.button !== 0) return
      if (event.clientY <= DRAG_REGION_HEIGHT && !isInteractiveElement(event.target)) {
        event.preventDefault()
        postHostMessage({ type: 'windowToggleMaximize' })
      }
    },
    { capture: true },
  )
}

if (!(window as HostWindow).chrome?.webview) {
  applyBackend(getBackendFromUrl())
}

installWindowChromeBridge()
;(window as HostWindow).__mihomoApplyBackend = (state) => {
  applyHostState(state)
}
;(window as HostWindow).chrome?.webview?.addEventListener?.('message', (event) => {
  if (event.data?.type === 'state') {
    applyHostState(event.data.state)
  }
})
