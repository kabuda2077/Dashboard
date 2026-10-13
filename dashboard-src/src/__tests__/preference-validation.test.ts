import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import type { Connection } from '@/types'

beforeEach(() => { vi.resetModules(); localStorage.clear() })
afterEach(() => { localStorage.clear(); window.dispatchEvent(new Event('pagehide')); vi.restoreAllMocks() })

it.each(['connection-sort-type', 'connection-sort-direction', 'connection-display-style', 'proxy-sort-type', 'log-level', 'connection-history-aggregation-type'])(
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
  const preferences = { 'config/connection-sort-type': 'host', 'config/connection-display-style': 'card', 'config/font': 'SystemUI', 'config/emoji': 'twemoji', 'config/disable-pull-to-refresh': 'true' }
  applyDashboardSettingsToStorage(parseSettingsDocument({ schemaVersion: 2, preferences }))
  expect(getDashboardSettingsFromStorage()).toEqual(preferences)
  expect(() => parseSettingsDocument({ schemaVersion: 1, preferences })).toThrow()
})

it.each([
  'not-json',
  '{}',
  '[{"id":"download","desc":"true"}]',
  '[{"id":"unknown","desc":true}]',
  '[{"id":"download","desc":true},{"id":"download","desc":false}]',
])('rejects malformed connection history sorting preferences before import (%s)', async (sorting) => {
  const { parseSettingsDocument } = await import('@/helper/utils')
  expect(() => parseSettingsDocument({ schemaVersion: 2, preferences: {
    'config/connection-history-sorting': sorting,
  } })).toThrow(/Invalid preference value/)
})
it.each(['[]', '[{"id":"key","desc":false},{"id":"total","desc":true}]'])(
  'round-trips connection history preferences through export selection, import and consumers (%s)', async (sorting) => {
    const { applyDashboardSettingsToStorage, parseSettingsDocument, getDashboardSettingsFromStorage } = await import('@/helper/utils')
    const preferences = {
      'config/connection-history-aggregation-type': 'destination',
      'config/connection-history-sorting': sorting,
    }
    applyDashboardSettingsToStorage(parseSettingsDocument({ schemaVersion: 2, preferences }))
    expect(getDashboardSettingsFromStorage()).toEqual(preferences)
    const { useStorage } = await import('@/composables/use-storage')
    expect(useStorage('config/connection-history-aggregation-type', 'sourceIP').value).toBe('destination')
    expect(useStorage('config/connection-history-sorting', [{ id: 'download', desc: true }]).value).toEqual(JSON.parse(sorting))
  },
)
it.each(['Win32', 'MacIntel', 'iPhone', 'Linux armv8l'])(
  'defaults to Noto without a platform-dependent emoji choice (%s)', async (platform) => {
    vi.spyOn(navigator, 'platform', 'get').mockReturnValue(platform)
    const { emoji } = await import('@/store/settings')
    expect(emoji.value).toBe('noto-color-emoji')
    expect(localStorage.getItem('config/emoji')).toBeNull()
  },
)

it.each(['twemoji', 'noto-color-emoji'])(
  'keeps a saved emoji choice (%s)', async (saved) => {
    localStorage.setItem('config/emoji', saved)
    const { emoji } = await import('@/store/settings')
    expect(emoji.value).toBe(saved)
    expect(localStorage.getItem('config/emoji')).toBe(saved)
  },
)

it('loads existing preferences containing retired theme names and a pull-to-refresh key', async () => {
  const { parseSettingsDocument, applyDashboardSettingsToStorage } = await import('@/helper/utils')
  applyDashboardSettingsToStorage(parseSettingsDocument({ schemaVersion: 2, preferences: {
    'config/default-theme': 'light-monet',
    'config/dark-theme': 'dark-monet',
    'config/disable-pull-to-refresh': 'true',
    'config/emoji': 'twemoji',
    'config/font': 'SystemUI',
  } }))
  const { defaultTheme, darkTheme, emoji, font } = await import('@/store/settings')
  expect(defaultTheme.value).toBe('light')
  expect(darkTheme.value).toBe('dark')
  expect(emoji.value).toBe('twemoji')
  expect(font.value).toBe('SystemUI')
  expect(localStorage.getItem('config/disable-pull-to-refresh')).toBe('true')
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
