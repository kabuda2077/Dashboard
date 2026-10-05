import { afterEach, expect, it, vi } from 'vitest'
import { effectScope, nextTick } from 'vue'
import { makeHostSnapshot, startMockHost } from './hostFixture'
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})
it('one hundred edits yield one preference commit; metadata and idle time create no writes', async () => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  const host = await startMockHost()
  host.post.mockImplementation((message) => queueMicrotask(() => host.ack(message.requestId)))
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  const { useDashboardStorage } = await import('@/helper/storage')
  const stop = installDashboardSettingsSync()
  for (let i = 0; i < 100; i++) {
    host.setState(makeHostSnapshot({ latestCoreVersion: String(i) }))
    await nextTick()
  }
  await vi.advanceTimersByTimeAsync(1300)
  expect(host.post).not.toHaveBeenCalled()
  const scope = effectScope()
  const value = scope.run(() => useDashboardStorage('config/write-measurement', 0))!
  for (let i = 1; i <= 100; i++) {
    value.value = i
    await nextTick()
  }
  await vi.advanceTimersByTimeAsync(301)
  expect(host.post).toHaveBeenCalledTimes(1)
  await vi.advanceTimersByTimeAsync(5000)
  expect(host.post).toHaveBeenCalledTimes(1)
  scope.stop()
  stop?.()
})
