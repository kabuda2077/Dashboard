import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const calls = vi.hoisted(() => ({ lookup: vi.fn() }))
vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  return { language: ref('zh-CN'), geoipCountryDatabaseURL: ref('country-old'), geoipASNDatabaseURL: ref('asn') }
})
vi.mock('@/helper/geoip-chunk-store', () => ({ geoIPChunkStore: {
  getManifest: async () => ({ updatedAt: Date.now(), generation: 'test' }),
  createSource: (url: string) => url,
} }))
vi.mock('@/helper/mmdb', () => ({ AsyncMMDBReader: {
  open: async (url: string) => ({ get: (ip: string) => calls.lookup(url, ip) }),
} }))

const settle = async () => { for (let i = 0; i < 12; i++) await Promise.resolve() }
beforeEach(() => { vi.resetModules(); vi.useFakeTimers(); calls.lookup.mockReset() })
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers() })

it('refreshes cached localized country names after a language change', async () => {
  const settings = await import('@/store/settings')
  const { LANG } = await import('@/constant')
  settings.language.value = LANG.ZH_CN
  calls.lookup.mockImplementation(async (url: string) => url === 'asn' ? {} : {
    country: { names: { en: 'China', 'zh-CN': '中国' } },
  })
  const { getConnectionGeoIPInfoSync: info } = await import('@/api/connection-geoip-database')
  info('8.8.8.8')
  await settle()
  expect(info('8.8.8.8').country).toBe('中国')
  settings.language.value = LANG.EN_US
  await vi.advanceTimersByTimeAsync(801)
  info('8.8.8.8')
  await settle()
  expect(info('8.8.8.8').country).toBe('China')
})

it('an old database lookup cannot populate the new cache or release its pending lookup', async () => {
  let oldResult!: (value: object) => void
  let newResult!: (value: object) => void
  calls.lookup.mockImplementation((url: string) => url === 'asn' ? Promise.resolve({}) :
    new Promise((resolve) => { if (url === 'country-old') oldResult = resolve; else newResult = resolve }))
  const settings = await import('@/store/settings')
  const { getConnectionGeoIPInfoSync: info } = await import('@/api/connection-geoip-database')
  info('8.8.8.8')
  await settle()
  settings.geoipCountryDatabaseURL.value = 'country-new'
  await vi.advanceTimersByTimeAsync(801)
  info('8.8.8.8')
  await settle()
  oldResult({ country: { iso_code: 'OLD' } })
  await settle()
  expect(info('8.8.8.8').country).toBe('')
  await settle()
  expect(calls.lookup).toHaveBeenCalledTimes(4)
  newResult({ country: { iso_code: 'NEW' } })
  await settle()
  expect(info('8.8.8.8').country).toBe('NEW')
})
