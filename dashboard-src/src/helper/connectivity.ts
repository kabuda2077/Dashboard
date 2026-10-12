import type { Backend } from '@/types'
import { getUrlFromBackend } from './utils'

export type ProbeFailureKind = 'unauthorized' | 'http' | 'timeout' | 'network'
export type ProbeResult = { ok: true } | { ok: false; kind: ProbeFailureKind; message?: string }
export type ConnectionDiagnosis =
  'offline' | 'corsBlocked' | 'mixedContent' | 'mixedContentOrUnreachable' | 'unreachable'

const diagnosisMessageKey: Record<ConnectionDiagnosis, string> = {
  offline: 'diagnosisOffline',
  corsBlocked: 'diagnosisCorsBlocked',
  mixedContent: 'diagnosisMixedContent',
  mixedContentOrUnreachable: 'diagnosisMixedContentOrUnreachable',
  unreachable: 'diagnosisUnreachable',
}

export const isOpaqueNetworkError = (message: string) =>
  ['Failed to fetch', 'Load failed', 'NetworkError', 'Network Error'].some((text) =>
    message.includes(text),
  )

export const isLoopbackHost = (hostname: string) =>
  hostname === 'localhost' ||
  hostname.endsWith('.localhost') ||
  hostname === '[::1]' ||
  hostname === '::1' ||
  /^127(\.\d{1,3}){3}$/.test(hostname)

export const getBackendProbeUrl = (
  backend: Pick<Backend, 'protocol' | 'host' | 'port' | 'secondaryPath'>,
) => {
  const url = new URL(getUrlFromBackend(backend))
  url.pathname = `${url.pathname.replace(/\/+$/, '')}/version`
  // Diagnostics never carry credentials, query tokens or fragment data.
  url.username = ''
  url.password = ''
  url.search = ''
  url.hash = ''
  return url.toString()
}

export const throwIfProbeAborted = (signal?: AbortSignal) => {
  if (signal?.aborted) throw new DOMException('Connection diagnosis cancelled', 'AbortError')
}

export const diagnoseConnection = async (
  url: string,
  signal?: AbortSignal,
): Promise<ConnectionDiagnosis> => {
  throwIfProbeAborted(signal)
  if (!navigator.onLine) return 'offline'
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), 5000)
  const onAbort = () => controller.abort()
  signal?.addEventListener('abort', onAbort, { once: true })
  try {
    await fetch(url, {
      mode: 'no-cors',
      cache: 'no-store',
      credentials: 'omit',
      signal: controller.signal,
    })
    throwIfProbeAborted(signal)
    return 'corsBlocked'
  } catch {
    // Caller cancellation is silent; only our bounded timeout becomes a diagnosis.
    throwIfProbeAborted(signal)
    if (location.protocol === 'https:' && url.startsWith('http:')) {
      try {
        return isLoopbackHost(new URL(url).hostname) ? 'mixedContentOrUnreachable' : 'mixedContent'
      } catch {
        return 'mixedContent'
      }
    }
    return 'unreachable'
  } finally {
    clearTimeout(timer)
    signal?.removeEventListener('abort', onAbort)
  }
}

export const describeProbeFailure = async (
  failure: { kind: ProbeFailureKind; message?: string },
  url: string,
  signal?: AbortSignal,
): Promise<string> => {
  throwIfProbeAborted(signal)
  const { i18n } = await import('@/i18n')
  throwIfProbeAborted(signal)
  const t = i18n.global.t
  switch (failure.kind) {
    case 'unauthorized':
      return t('diagnosisUnauthorized')
    case 'http':
      return `${failure.message || 'HTTP error'} — ${t('diagnosisBadEndpoint')}`
    case 'timeout':
      return t('diagnosisTimeout')
    default:
      return t(diagnosisMessageKey[await diagnoseConnection(url, signal)])
  }
}

export const describeConnectionError = async (
  message: string,
  url: string,
  signal?: AbortSignal,
): Promise<string> => {
  throwIfProbeAborted(signal)
  if (!isOpaqueNetworkError(message)) return message
  const diagnosis = await diagnoseConnection(url, signal)
  const { i18n } = await import('@/i18n')
  throwIfProbeAborted(signal)
  return i18n.global.t(diagnosisMessageKey[diagnosis])
}
