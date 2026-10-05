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
it('browser startup does not require a desktop host', async () => {
  Reflect.deleteProperty(window, 'chrome')
  const { startDashboard } = await import('@/helper/dashboardStartup')
  const load = vi.fn(async () => {})
  await startDashboard(load)
  expect(load).toHaveBeenCalledOnce()
})
