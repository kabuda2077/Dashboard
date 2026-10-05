import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { startMockHost } from './hostFixture'

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
})
afterEach(() => {
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})
it('waits for the matching current-protocol persistence result', async () => {
  const host = await startMockHost()
  const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
  localStorage.setItem('config/theme', 'dark')
  const done = vi.fn()
  const save = saveDashboardSettingsToHost().then(done)
  expect(host.post.mock.lastCall?.[0]).toMatchObject({
    type: 'saveDashboardPreferences',
    protocolVersion: 2,
    preferences: { 'config/theme': 'dark' },
  })
  host.ack('wrong-id')
  await Promise.resolve()
  expect(done).not.toHaveBeenCalled()
  host.ack()
  await save
  expect(done).toHaveBeenCalledOnce()
})
it('coalesces intermediate snapshots but completes their waiters only after a newer commit', async () => {
  const host = await startMockHost()
  const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
  localStorage.setItem('config/theme', 'first')
  const first = saveDashboardSettingsToHost()
  localStorage.setItem('config/theme', 'middle')
  const middle = saveDashboardSettingsToHost()
  localStorage.setItem('config/theme', 'latest')
  const latest = saveDashboardSettingsToHost()
  expect(host.post).toHaveBeenCalledTimes(1)
  host.ack()
  await first
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post).toHaveBeenCalledTimes(2)
  expect(host.post.mock.lastCall?.[0].preferences['config/theme']).toBe('latest')
  host.ack()
  await Promise.all([middle, latest])
})
it('a failed save remains retryable and is not acknowledged by de-duplication', async () => {
  const host = await startMockHost()
  const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
  localStorage.setItem('config/theme', 'same')
  const first = saveDashboardSettingsToHost().catch((error) => error)
  host.ack(undefined, false)
  expect(await first).toBeInstanceOf(Error)
  const retry = saveDashboardSettingsToHost()
  host.ack()
  await retry
  expect(host.post).toHaveBeenCalledTimes(2)
  await saveDashboardSettingsToHost()
  expect(host.post).toHaveBeenCalledTimes(2)
})
it('timeout rejects instead of pretending disk save succeeded', async () => {
  await startMockHost()
  const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
  const saved = saveDashboardSettingsToHost().catch((error) => error)
  await vi.advanceTimersByTimeAsync(30001)
  expect(await saved).toBeInstanceOf(Error)
})
