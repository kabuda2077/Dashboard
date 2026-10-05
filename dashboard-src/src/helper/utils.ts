import { MIN_PROXY_CARD_WIDTH, PROXY_CARD_SIZE } from '@/constant'
import type { Backend } from '@/types'
import { useMediaQuery } from '@vueuse/core'
import dayjs from 'dayjs'
import prettyBytes, { type Options } from 'pretty-bytes'
import { markPreferenceStorageFailed, notifyDashboardSettingsChanged } from './settingsChanges'
import { validatePreferenceValues } from './preferenceValues'

export const isPreferredDark = useMediaQuery('(prefers-color-scheme: dark)')
export const isMiddleScreen = useMediaQuery('(max-width: 768px)')

export const prettyBytesHelper = (bytes: number, opts?: Options) => {
  return prettyBytes(Number.isFinite(bytes) ? bytes : 0, {
    binary: false,
    ...opts,
  })
}

export const fromNow = (timestamp: string | number) => {
  return dayjs(timestamp).fromNow()
}

export const isDashboardSettingKey = (key: string | null | undefined): key is string => {
  return !!key && key.startsWith('config/')
}

export const getDashboardSettingsFromStorage = () => {
  const settings: Record<string, string> = {}

  for (let index = 0; index < localStorage.length; index += 1) {
    const key = localStorage.key(index)
    if (isDashboardSettingKey(key)) {
      const value = localStorage.getItem(key)
      if (typeof value === 'string') {
        settings[key] = value
      }
    }
  }

  return settings
}

export const applyDashboardSettingsToStorage = (
  settings: Record<string, unknown>,
  replace = false,
) => {
  validatePreferenceValues(settings)
  const before = getDashboardSettingsFromStorage()
  try {
    if (replace)
      for (const key of Object.keys(before))
        if (!Object.hasOwn(settings, key)) localStorage.removeItem(key)
    for (const [key, value] of Object.entries(settings)) {
      if (isDashboardSettingKey(key) && typeof value === 'string') localStorage.setItem(key, value)
    }
  } catch (error) {
    try {
      for (const key of Object.keys(getDashboardSettingsFromStorage()))
        if (!Object.hasOwn(before, key)) localStorage.removeItem(key)
      for (const [key, value] of Object.entries(before)) localStorage.setItem(key, value)
    } catch {
      // Never send a partially restored document back over the durable host snapshot.
      markPreferenceStorageFailed()
      throw new Error(
        'Local preference storage failed to roll back. Reopen the window to restore the host snapshot.',
      )
    }
    throw error
  }
  notifyDashboardSettingsChanged(null)
}

export const clearDashboardSettingsFromStorage = () => applyDashboardSettingsToStorage({}, true)

export const parseSettingsDocument = (input: unknown): Record<string, string> => {
  if (!input || typeof input !== 'object' || Array.isArray(input))
    throw new Error('Invalid settings document')
  const document = input as { schemaVersion?: unknown; preferences?: unknown }
  const values = document.preferences
  if (
    document.schemaVersion !== 2 ||
    !values ||
    typeof values !== 'object' ||
    Array.isArray(values) ||
    !Object.entries(values).every(
      ([key, value]) => key.startsWith('config/') && typeof value === 'string',
    )
  ) {
    throw new Error('Only schemaVersion=2 preference documents are supported')
  }
  validatePreferenceValues(values as Record<string, unknown>)
  return { ...values } as Record<string, string>
}

export const exportSettings = () => {
  const settings = { schemaVersion: 2, preferences: getDashboardSettingsFromStorage() }
  const blob = new Blob([JSON.stringify(settings, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'zashboard-settings'
  a.click()
  URL.revokeObjectURL(url)
}

export const getUrlFromBackend = (end: {
  protocol: string
  host: string
  port: string
  secondaryPath?: string
}) => {
  return `${end.protocol}://${end.host}:${end.port}${end.secondaryPath || ''}`
}

export const getLabelFromBackend = (end: Omit<Backend, 'uuid'>) => {
  return end.label || `${end.host}:${end.port}`
}

export const getMinCardWidth = (size: PROXY_CARD_SIZE) => {
  return size === PROXY_CARD_SIZE.LARGE ? MIN_PROXY_CARD_WIDTH.LARGE : MIN_PROXY_CARD_WIDTH.SMALL
}

export const PROXIES_PARENT_CLASS = 'proxies-scrollable-parent'

export const getBackendFromUrl = () => {
  const query = new URLSearchParams(
    window.location.search || location.hash.match(/\?.*$/)?.[0]?.replace('?', ''),
  )

  const host = query.get('hostname')
  const protocol = query.get('protocol') ?? window.location.protocol.replace(':', '')
  if (
    !host ||
    !['http', 'https'].includes(protocol) ||
    /[\/@?#]/.test(host) ||
    (query.has('type') && query.get('type') !== 'clash')
  )
    return null
  try {
    const port = query.get('port')
    const address = new URL(`${protocol}://${host}${port ? ':' + port : ''}`)
    return {
      type: 'clash' as const,
      protocol,
      secondaryPath: query.get('secondaryPath') || '',
      host: address.hostname,
      port: address.port || (protocol === 'https' ? '443' : '80'),
      password: query.get('secret') || '',
      label: query.get('label') || '',
      disableUpgradeCore: query.get('disableUpgradeCore') === '1',
      disableTunMode: query.get('disableTunMode') === '1',
    }
  } catch {
    return null
  }
}
