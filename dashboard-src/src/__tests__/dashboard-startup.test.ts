import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { bootstrapFixture, installHostMock, makeHostSnapshot } from './hostFixture'

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  document.body.innerHTML = '<div id="app"></div>'
})
afterEach(() => {
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})
it('uses the real C# bootstrap shape and restores preferences before importing stores', async () => {
  const host = installHostMock()
  const { startDashboard } = await import('@/helper/dashboardStartup')
  localStorage.setItem('config/old', 'old')
  const load = vi.fn(async () => {
    expect(localStorage.getItem('config/theme')).toBe('dark')
    expect(localStorage.getItem('config/old')).toBeNull()
  })
  const result = startDashboard(load)
  host.bootstrap('wrong-id', { 'config/theme': 'wrong' })
  expect(load).not.toHaveBeenCalled()
  host.bootstrap(undefined, { 'config/theme': 'dark' })
  await result
  expect(load).toHaveBeenCalledOnce()
  expect(bootstrapFixture().protocolVersion).toBe(2)
})
it.each([null, undefined, { 'setup/password': 'bad' }])(
  'rejects unsupported preference payloads without changing storage: %j',
  async (preferences) => {
    const host = installHostMock()
    const { restoreDashboardSettings } = await import('@/helper/dashboardStartup')
    localStorage.setItem('config/theme', 'kept')
    const result = restoreDashboardSettings().catch((error) => error)
    if (preferences === undefined)
      host.emit({
        type: 'bootstrap',
        requestId: host.post.mock.lastCall?.[0].requestId,
        state: makeHostSnapshot(),
      })
    else host.bootstrap(undefined, preferences)
    expect(await result).toBeInstanceOf(Error)
    expect(localStorage.getItem('config/theme')).toBe('kept')
  },
)
it('supports a first installation with an explicit empty snapshot', async () => {
  const host = installHostMock()
  const { startDashboard } = await import('@/helper/dashboardStartup')
  const load = vi.fn(async () => {})
  const boot = startDashboard(load)
  host.bootstrap()
  await boot
  expect(load).toHaveBeenCalledOnce()
})
it('fails visibly on timeout and a fresh request can retry', async () => {
  const host = installHostMock()
  const { startDashboard } = await import('@/helper/dashboardStartup')
  const load = vi.fn(async () => {})
  const boot = startDashboard(load)
  await vi.advanceTimersByTimeAsync(30001)
  await boot
  expect(load).not.toHaveBeenCalled()
  document.querySelector('button')!.click()
  host.bootstrap()
  await vi.advanceTimersByTimeAsync(0)
  expect(load).toHaveBeenCalledOnce()
})
it('commits valid legacy history preferences before loading stores or deleting cache keys', async () => {
  const host = installHostMock()
  const { startDashboard } = await import('@/helper/dashboardStartup')
  const sorting = JSON.stringify([{ id: 'upload', desc: false }])
  localStorage.setItem('cache/connection-history-aggregation-type', 'destination')
  localStorage.setItem('cache/connection-history-sorting', sorting)
  localStorage.setItem('cache/connection-history-stats-start-time', '123')
  const load = vi.fn(async () => {})
  const boot = startDashboard(load)
  host.bootstrap(undefined, { 'config/font': 'SystemUI' })
  await vi.advanceTimersByTimeAsync(0)
  const preferences = {
    'config/font': 'SystemUI',
    'config/connection-history-aggregation-type': 'destination',
    'config/connection-history-sorting': sorting,
  }
  expect(host.post.mock.lastCall?.[0]).toMatchObject({ type: 'saveDashboardPreferences', preferences })
  expect(load).not.toHaveBeenCalled()
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBe('destination')
  host.ack('wrong-save-id')
  await Promise.resolve()
  expect(load).not.toHaveBeenCalled()
  host.ack()
  await boot
  expect(load).toHaveBeenCalledOnce()
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBeNull()
  expect(localStorage.getItem('cache/connection-history-sorting')).toBeNull()
  expect(localStorage.getItem('cache/connection-history-stats-start-time')).toBe('123')
  const { getDashboardSettingsFromStorage } = await import('@/helper/utils')
  expect(getDashboardSettingsFromStorage()).toEqual(preferences)
  expect((await import('@/helper/dashboardStartup')).restoredPreferences).toEqual(preferences)
})
it.each(['failed', 'timeout'])('preserves legacy history values after a %s save and retries startup', async (failure) => {
  const host = installHostMock()
  const { restoreDashboardSettings } = await import('@/helper/dashboardStartup')
  localStorage.setItem('cache/connection-history-aggregation-type', 'process')
  localStorage.setItem('config/font', 'SystemUI')
  const boot = restoreDashboardSettings().catch((error) => error)
  host.bootstrap()
  await vi.advanceTimersByTimeAsync(0)
  if (failure === 'timeout') await vi.advanceTimersByTimeAsync(30001)
  else host.ack(undefined, false)
  expect(await boot).toBeInstanceOf(Error)
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBe('process')
  expect(localStorage.getItem('config/connection-history-aggregation-type')).toBeNull()
  expect(localStorage.getItem('config/font')).toBe('SystemUI')
  const retry = restoreDashboardSettings()
  host.bootstrap()
  await vi.advanceTimersByTimeAsync(0)
  host.ack()
  await retry
  expect(localStorage.getItem('config/connection-history-aggregation-type')).toBe('process')
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBeNull()
})
it('prefers saved history preferences over old cache values without another host save', async () => {
  const host = installHostMock()
  const { restoreDashboardSettings } = await import('@/helper/dashboardStartup')
  localStorage.setItem('cache/connection-history-aggregation-type', 'destination')
  localStorage.setItem('cache/connection-history-sorting', JSON.stringify([{ id: 'upload', desc: true }]))
  const preferences = {
    'config/connection-history-aggregation-type': 'outbound',
    'config/connection-history-sorting': '[]',
  }
  const boot = restoreDashboardSettings()
  host.bootstrap(undefined, preferences)
  await boot
  expect(host.post).toHaveBeenCalledOnce()
  expect(localStorage.getItem('config/connection-history-aggregation-type')).toBe('outbound')
  expect(localStorage.getItem('config/connection-history-sorting')).toBe('[]')
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBeNull()
  expect(localStorage.getItem('cache/connection-history-sorting')).toBeNull()
})
it('migrates only the missing history key and does not promote stale local config into the host snapshot', async () => {
  const host = installHostMock()
  const { restoreDashboardSettings } = await import('@/helper/dashboardStartup')
  localStorage.setItem('config/connection-history-aggregation-type', 'process')
  localStorage.setItem('config/font', 'MiSans')
  localStorage.setItem('cache/connection-history-aggregation-type', 'destination')
  localStorage.setItem('cache/connection-history-sorting', '[]')
  const boot = restoreDashboardSettings()
  host.bootstrap(undefined, { 'config/connection-history-aggregation-type': 'outbound', 'config/unknown-future-option': 'kept' })
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post.mock.lastCall?.[0].preferences).toEqual({
    'config/connection-history-aggregation-type': 'outbound',
    'config/connection-history-sorting': '[]',
    'config/unknown-future-option': 'kept',
  })
  host.ack()
  await boot
  expect(localStorage.getItem('config/font')).toBeNull()
})
it('recovers from a local mirror failure using the committed host snapshot without another save', async () => {
  const host = installHostMock()
  const { startDashboard } = await import('@/helper/dashboardStartup')
  localStorage.setItem('cache/connection-history-aggregation-type', 'destination')
  const load = vi.fn(async () => {})
  const boot = startDashboard(load)
  host.bootstrap()
  await vi.advanceTimersByTimeAsync(0)
  const persisted = { ...host.post.mock.lastCall?.[0].preferences }
  const write = vi.spyOn(localStorage, 'setItem').mockImplementationOnce(() => { throw new Error('Storage full') })
  host.ack()
  await boot
  write.mockRestore()
  expect(load).not.toHaveBeenCalled()
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBe('destination')
  const retry = startDashboard(load)
  host.bootstrap(undefined, persisted)
  await retry
  expect(load).toHaveBeenCalledOnce()
  expect(host.post.mock.calls.filter(([message]) => message.type === 'saveDashboardPreferences')).toHaveLength(1)
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBeNull()
})
it('does not treat a completed command without saved confirmation as durable migration', async () => {
  const host = installHostMock()
  const { restoreDashboardSettings } = await import('@/helper/dashboardStartup')
  localStorage.setItem('cache/connection-history-aggregation-type', 'destination')
  const boot = restoreDashboardSettings().catch((error) => error)
  host.bootstrap()
  await vi.advanceTimersByTimeAsync(0)
  host.emit({ type: 'commandResult', requestId: host.post.mock.lastCall?.[0].requestId, state: makeHostSnapshot(), result: { status: 'completed', code: 'notSaved' } })
  expect(await boot).toBeInstanceOf(Error)
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBe('destination')
  expect(localStorage.getItem('config/connection-history-aggregation-type')).toBeNull()
})
it('does not migrate malformed or unsupported history cache values', async () => {
  const host = installHostMock()
  const { restoreDashboardSettings } = await import('@/helper/dashboardStartup')
  localStorage.setItem('cache/connection-history-aggregation-type', 'invalid')
  localStorage.setItem('cache/connection-history-sorting', '[{"id":"unknown","desc":true}]')
  const boot = restoreDashboardSettings()
  host.bootstrap()
  await boot
  expect(host.post).toHaveBeenCalledOnce()
  expect(localStorage.getItem('config/connection-history-aggregation-type')).toBeNull()
  expect(localStorage.getItem('config/connection-history-sorting')).toBeNull()
})
it('migrates browser history preferences locally and keeps an existing new value', async () => {
  Reflect.deleteProperty(window, 'chrome')
  localStorage.setItem('config/connection-history-aggregation-type', 'proxyGroup')
  localStorage.setItem('cache/connection-history-aggregation-type', 'destination')
  localStorage.setItem('cache/connection-history-sorting', '[]')
  localStorage.setItem('config/font', 'SystemUI')
  const { restoreDashboardSettings } = await import('@/helper/dashboardStartup')
  await restoreDashboardSettings()
  expect(localStorage.getItem('config/connection-history-aggregation-type')).toBe('proxyGroup')
  expect(localStorage.getItem('config/connection-history-sorting')).toBe('[]')
  expect(localStorage.getItem('cache/connection-history-aggregation-type')).toBeNull()
  expect(localStorage.getItem('cache/connection-history-sorting')).toBeNull()
  expect(localStorage.getItem('config/font')).toBe('SystemUI')
})
it('browser startup does not require a desktop host', async () => {
  Reflect.deleteProperty(window, 'chrome')
  const { startDashboard } = await import('@/helper/dashboardStartup')
  const load = vi.fn(async () => {})
  await startDashboard(load)
  expect(load).toHaveBeenCalledOnce()
})
