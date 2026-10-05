import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { startMockHost } from './hostFixture'

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  sessionStorage.clear()
})
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})
it('keeps defaults absent, observes real writes/imports/cross-document changes, and stops listening', async () => {
  const host = await startMockHost()
  host.post.mockImplementation((message) => queueMicrotask(() => host.ack(message.requestId)))
  const { useDashboardStorage } = await import('@/helper/storage')
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  const scope = effectScope()
  const value = scope.run(() => useDashboardStorage('config/test', 'default'))!
  const dispose = installDashboardSettingsSync()!
  await vi.advanceTimersByTimeAsync(301)
  expect(localStorage.getItem('config/test')).toBeNull()
  expect(host.post).not.toHaveBeenCalled()
  value.value = 'changed'
  await nextTick()
  await vi.advanceTimersByTimeAsync(301)
  expect(host.post.mock.lastCall?.[0].preferences['config/test']).toBe('changed')
  const { applyDashboardSettingsToStorage } = await import('@/helper/utils')
  applyDashboardSettingsToStorage({ 'config/imported': 'yes' })
  await vi.advanceTimersByTimeAsync(301)
  expect(host.post.mock.lastCall?.[0].preferences['config/imported']).toBe('yes')
  localStorage.setItem('config/remote', 'external')
  window.dispatchEvent(
    new StorageEvent('storage', {
      key: 'config/remote',
      storageArea: localStorage,
      newValue: 'external',
    }),
  )
  await vi.advanceTimersByTimeAsync(301)
  expect(host.post.mock.lastCall?.[0].preferences['config/remote']).toBe('external')
  const count = host.post.mock.calls.length
  dispose()
  scope.stop()
  localStorage.setItem('config/remote', 'later')
  window.dispatchEvent(
    new StorageEvent('storage', { key: 'config/remote', storageArea: localStorage }),
  )
  await vi.advanceTimersByTimeAsync(301)
  expect(host.post).toHaveBeenCalledTimes(count)
})
it('reactive keys and explicit non-preference defaults retain their distinct semantics', async () => {
  const host = await startMockHost()
  host.post.mockImplementation((message) => queueMicrotask(() => host.ack(message.requestId)))
  const { useDashboardStorage } = await import('@/helper/storage')
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  const scope = effectScope()
  const key = ref('config/first')
  const value = scope.run(() => useDashboardStorage(key, 'default'))!
  const stamp = scope.run(() =>
    useDashboardStorage('cache/stamp', 123, undefined, { writeDefaults: true }),
  )!
  installDashboardSettingsSync()
  expect(localStorage.getItem('cache/stamp')).toBe('123')
  expect(stamp.value).toBe(123)
  key.value = 'config/second'
  await nextTick()
  expect(localStorage.getItem('config/second')).toBeNull()
  value.value = 'edited'
  await nextTick()
  await vi.advanceTimersByTimeAsync(301)
  expect(host.post.mock.lastCall?.[0].preferences).toEqual({ 'config/second': 'edited' })
  scope.stop()
})
it.each([
  ['config/table-column-width', {}, { host: 120 }],
  ['config/table-grouping', [], ['Host']],
  ['config/logs-table-sorting', [], [{ id: 'time', desc: true }]],
  ['config/proxy-folders', {}, { group: ['a'] }],
  ['config/log-filter-enabled', false, true],
  ['config/quick-filter-regex', '', 'direct'],
])('serializes a burst once using the native storage adapter: %s', async (key, initial, next) => {
  const host = await startMockHost()
  host.post.mockImplementation((message) => queueMicrotask(() => host.ack(message.requestId)))
  const { useDashboardStorage } = await import('@/helper/storage')
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  const scope = effectScope()
  const value = scope.run(() => useDashboardStorage<unknown>(key as string, initial))!
  installDashboardSettingsSync()
  expect(localStorage.getItem(key as string)).toBeNull()
  value.value = next
  await nextTick()
  await vi.advanceTimersByTimeAsync(301)
  expect(host.post).toHaveBeenCalledTimes(1)
  expect(host.post.mock.lastCall?.[0].preferences[key as string]).toBe(
    localStorage.getItem(key as string),
  )
  scope.stop()
})
