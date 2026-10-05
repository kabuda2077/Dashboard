import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { Connection } from '@/types'

beforeEach(() => { vi.resetModules(); localStorage.clear() })
afterEach(() => { localStorage.clear(); window.dispatchEvent(new Event('pagehide')) })

it.each(['connection-sort-type', 'connection-sort-direction', 'connection-display-style', 'proxy-sort-type', 'log-level'])(
  'rejects an invalid current enum before import changes storage (%s)', async (key) => {
    const { parseSettingsDocument, applyDashboardSettingsToStorage } = await import('@/helper/utils')
    localStorage.setItem('config/font', 'SystemUI')
    const invalid = { [`config/${key}`]: 'invalid-current-value', 'config/font': 'MiSans' }
    expect(() => parseSettingsDocument({ schemaVersion: 2, preferences: invalid })).toThrow(/Invalid preference value/)
    expect(() => applyDashboardSettingsToStorage(invalid, true)).toThrow(/Invalid preference value/)
    expect(localStorage.getItem('config/font')).toBe('SystemUI')
    expect(localStorage.getItem(`config/${key}`)).toBeNull()
  },
)

it('valid raw enum preferences round-trip without quoting or legacy migration', async () => {
  const { parseSettingsDocument, applyDashboardSettingsToStorage, getDashboardSettingsFromStorage } = await import('@/helper/utils')
  const preferences = { 'config/connection-sort-type': 'host', 'config/connection-display-style': 'card', 'config/font': 'SystemUI' }
  applyDashboardSettingsToStorage(parseSettingsDocument({ schemaVersion: 2, preferences }))
  expect(getDashboardSettingsFromStorage()).toEqual(preferences)
  expect(() => parseSettingsDocument({ schemaVersion: 1, preferences })).toThrow()
})

it('an invalid stored or subsequently assigned sort value cannot break live connection rendering', async () => {
  localStorage.setItem('config/connection-sort-type', 'invalid-current-value')
  localStorage.setItem('config/connection-display-style', 'card')
  const store = await import('@/store/connections')
  const settings = await import('@/store/settings')
  const { SORT_TYPE } = await import('@/constant')
  expect(store.connectionSortType.value).toBe(SORT_TYPE.HOST)
  expect(settings.isConnectionCard.value).toBe(true)
  store.activeConnections.value = [{ id: 'one', metadata: { host: 'example.test', sourceIP: '127.0.0.1', destinationIP: '1.1.1.1', network: 'tcp' }, chains: ['DIRECT'], rule: 'Match', rulePayload: '', downloadSpeed: 1, uploadSpeed: 0, download: 1, upload: 0 } as Connection]
  store.connectionSortType.value = 'invalid-current-value' as typeof store.connectionSortType.value
  expect(() => store.renderConnections.value).not.toThrow()
  expect(store.renderConnections.value).toHaveLength(1)
})
