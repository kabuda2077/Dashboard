import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { startMockHost } from './hostFixture'

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
})
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})

it('includes preference edits made after a native flush has started but before its disk ACK', async () => {
  const host = await startMockHost()
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  installDashboardSettingsSync()
  localStorage.setItem('config/theme', 'first')
  host.emit({ type: 'flushPreferences', requestId: 'native-race' })
  await vi.advanceTimersByTimeAsync(0)
  const first = host.post.mock.calls[0][0]
  expect(first.preferences['config/theme']).toBe('first')
  localStorage.setItem('config/theme', 'changed-during-save')
  host.ack(first.requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post.mock.calls.some(([m]) => m.type === 'preferencesFlushed')).toBe(false)
  const second = host.post.mock.lastCall![0]
  expect(second.preferences['config/theme']).toBe('changed-during-save')
  host.ack(second.requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post.mock.lastCall![0]).toMatchObject({
    protocolVersion: 2, type: 'preferencesFlushed', requestId: 'native-race', value: true,
  })
})

it('does not send a flush acknowledgement after the document has been disposed', async () => {
  const host = await startMockHost()
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  const dispose = installDashboardSettingsSync()!
  localStorage.setItem('config/theme', 'pending')
  host.emit({ type: 'flushPreferences', requestId: 'old-document' })
  await vi.advanceTimersByTimeAsync(0)
  dispose()
  host.ack(host.post.mock.calls[0][0].requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(host.post.mock.calls.some(([m]) => m.type === 'preferencesFlushed')).toBe(false)
})

it('refuses native release when the v2 persistence acknowledgement times out', async () => {
  const host = await startMockHost()
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  installDashboardSettingsSync()
  localStorage.setItem('config/theme', 'pending')
  host.emit({ type: 'flushPreferences', requestId: 'timed-out' })
  await vi.advanceTimersByTimeAsync(30001)
  await vi.waitFor(() => expect(host.post.mock.lastCall![0]).toMatchObject({
    type: 'preferencesFlushed', requestId: 'timed-out', value: false,
  }))
})
