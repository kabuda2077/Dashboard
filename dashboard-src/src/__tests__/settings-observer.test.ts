import { afterEach, expect, it, vi } from 'vitest'
import { createApp, effectScope, nextTick, ref } from 'vue'

let stop: (() => void) | undefined
let scope = effectScope()
afterEach(() => {
  stop?.(); stop = undefined; scope.stop(); scope = effectScope()
  vi.useRealTimers(); Reflect.deleteProperty(window, 'chrome')
  Reflect.deleteProperty(window, '__mihomoHasDashboardSettings')
  Reflect.deleteProperty(window, '__mihomoDashboardSettings')
})

const install = async () => {
  vi.resetModules(); vi.useFakeTimers(); localStorage.clear()
  let receive: (event: MessageEvent) => void = () => {}
  const post = vi.fn((message) => queueMicrotask(() => receive(new MessageEvent('message', {
    data: { type: 'dashboardSettingsSaved', requestId: message.requestId, success: true },
  }))))
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: post, addEventListener: (_: string, fn: typeof receive) => { receive = fn }, removeEventListener: vi.fn(),
  } } })
  const { installDashboardSettingsSync } = await import('@/helper/dashboardSettingsSync')
  stop = installDashboardSettingsSync()
  expect(installDashboardSettingsSync()).toBe(stop)
  return post
}

it('observes native writes, imports, and cross-document changes without polling, then disposes', async () => {
  const post = await install()
  const { useDashboardStorage } = await import('@/helper/storage')
  const preference = scope.run(() => useDashboardStorage('config/test', 'default'))!
  preference.value = 'changed'; await nextTick()
  await vi.advanceTimersByTimeAsync(301)
  expect(post.mock.lastCall?.[0].settings['config/test']).toBe('changed')
  expect(post).toHaveBeenCalledTimes(1)
  const { applyDashboardSettingsToStorage } = await import('@/helper/utils')
  applyDashboardSettingsToStorage({ 'config/imported': 'value' })
  await vi.advanceTimersByTimeAsync(301)
  expect(post.mock.lastCall?.[0].settings).toMatchObject({ 'config/test': 'changed', 'config/imported': 'value' })
  localStorage.setItem('config/test', 'other-document')
  window.dispatchEvent(new StorageEvent('storage', { key: 'config/test', newValue: 'other-document', storageArea: localStorage }))
  await nextTick(); await vi.advanceTimersByTimeAsync(301)
  expect(preference.value).toBe('other-document')
  expect(post.mock.lastCall?.[0].settings['config/test']).toBe('other-document')
  const count = post.mock.calls.length
  await vi.advanceTimersByTimeAsync(5000)
  expect(post).toHaveBeenCalledTimes(count)
  expect(vi.getTimerCount()).toBe(0)
  stop?.(); preference.value = 'after-dispose'
  await nextTick(); await vi.advanceTimersByTimeAsync(2000)
  expect(post).toHaveBeenCalledTimes(count)
})

it('saves lazy and deferred defaults and reactive key changes without a periodic scan', async () => {
  const post = await install()
  await vi.advanceTimersByTimeAsync(301)
  const { useDashboardStorage } = await import('@/helper/storage')
  const key = ref('config/lazy-first')
  scope.run(() => useDashboardStorage(key, 'default'))
  await vi.advanceTimersByTimeAsync(301)
  expect(post.mock.lastCall?.[0].settings['config/lazy-first']).toBe('default')
  key.value = 'config/lazy-second'; await nextTick()
  await vi.advanceTimersByTimeAsync(301)
  expect(post.mock.lastCall?.[0].settings['config/lazy-second']).toBe('default')
  const app = createApp({ setup() {
    useDashboardStorage('config/deferred', 'mounted', undefined, { initOnMounted: true })
    return () => null
  } })
  app.mount(document.createElement('div'))
  try {
    await nextTick(); await vi.advanceTimersByTimeAsync(301)
    expect(post.mock.lastCall?.[0].settings['config/deferred']).toBe('mounted')
    expect(vi.getTimerCount()).toBe(0)
  } finally { app.unmount() }
})

it.each([
  ['config/table-column-width', { host: 120 }, { host: 180 }],
  ['config/table-grouping', [], ['host']],
  ['config/logs-table-sorting', [], [{ id: 'time', desc: true }]],
  ['config/proxy-folders', { folders: [], seeded: false }, { folders: [], seeded: true }],
  ['config/log-filter-enabled', false, true],
  ['config/quick-filter-regex', 'direct', 'dns'],
  ['config/connection-card-group-key', null, 'host'],
  ['config/import-settings-url', '', 'https://example.test/settings'],
  ['config/connection-history-auto-cleanup-interval', 0, 7],
])('preserves serialization/defaults and saves one burst for %s', async (key, initial, changed) => {
  const post = await install()
  const { useDashboardStorage } = await import('@/helper/storage')
  const preference = scope.run(() => useDashboardStorage<unknown>(key, initial, localStorage))!
  if (initial === null) expect(localStorage.getItem(key)).toBeNull()
  else expect(localStorage.getItem(key)).not.toBeNull()
  preference.value = changed; await nextTick()
  await vi.advanceTimersByTimeAsync(301)
  expect(post).toHaveBeenCalledTimes(1)
  expect(post.mock.lastCall?.[0].settings[key]).toBe(localStorage.getItem(key))
})

it('keeps absent defaults absent and preserves explicit serializers, merge options and session isolation', async () => {
  const post = await install()
  const { useStorage, useDashboardStorage } = await import('@/helper/storage')
  scope.run(() => useStorage('config/hidden-group-map', {}))
  expect(localStorage.getItem('config/hidden-group-map')).toBeNull()
  localStorage.setItem('config/proxy-folders', '{"seeded":true}')
  const folders = scope.run(() => useDashboardStorage('config/proxy-folders', { seeded: false, folders: [] }, localStorage, { mergeDefaults: true }))!
  expect(folders.value).toEqual({ seeded: true, folders: [] })
  const custom = scope.run(() => useDashboardStorage('config/custom', 1, undefined, { writeDefaults: false, serializer: { read: Number, write: v => String(v * 2) } }))!
  expect(localStorage.getItem('config/custom')).toBeNull()
  custom.value = 3; await nextTick()
  expect(localStorage.getItem('config/custom')).toBe('6')
  const session = scope.run(() => useDashboardStorage('cache/session', false, sessionStorage))!
  await vi.advanceTimersByTimeAsync(301)
  const count = post.mock.calls.length
  session.value = true; await nextTick(); await vi.advanceTimersByTimeAsync(301)
  expect(post).toHaveBeenCalledTimes(count)
  expect(post.mock.lastCall?.[0].settings).not.toHaveProperty('cache/session')
})
