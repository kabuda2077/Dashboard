import { beforeEach, afterEach, expect, it, vi } from 'vitest'

const notify = vi.fn()
vi.mock('@/helper/requestError', () => ({ notifyRequestError: notify }))
let listeners: Set<(event: MessageEvent) => void>
let post: ReturnType<typeof vi.fn>
beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  notify.mockClear()
  listeners = new Set()
  post = vi.fn()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: post,
    addEventListener: (_: string, fn: (event: MessageEvent) => void) => listeners.add(fn),
    removeEventListener: (_: string, fn: (event: MessageEvent) => void) => listeners.delete(fn),
  } } })
})
afterEach(() => { vi.useRealTimers(); Reflect.deleteProperty(window, 'chrome') })
const ack = (requestId: string, success = true) => listeners.forEach((fn) => fn(new MessageEvent('message', {
  data: { type: 'dashboardSettingsSaved', requestId, success },
})))

it('waits for the matching disk-save acknowledgement, not a fixed reload delay', async () => {
  const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
  const done = vi.fn()
  const promise = saveDashboardSettingsToHost({ beforeReload: true }).then(done)
  await vi.advanceTimersByTimeAsync(100)
  expect(post).toHaveBeenCalledTimes(1)
  expect(done).not.toHaveBeenCalled()
  ack('other-document')
  await Promise.resolve()
  expect(done).not.toHaveBeenCalled()
  ack(post.mock.calls[0][0].requestId)
  await promise
  expect(done).toHaveBeenCalledOnce()
  expect(listeners.size).toBe(0)
})

it('serializes captured snapshots and recovers after a failed save', async () => {
  const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
  localStorage.setItem('config/theme', 'old')
  const first = saveDashboardSettingsToHost().catch((error: Error) => error)
  localStorage.setItem('config/theme', 'new')
  const second = saveDashboardSettingsToHost()
  await vi.advanceTimersByTimeAsync(0)
  expect(post).toHaveBeenCalledTimes(1)
  expect(post.mock.calls[0][0].settings['config/theme']).toBe('old')
  ack(post.mock.calls[0][0].requestId, false)
  expect(await first).toBeInstanceOf(Error)
  await vi.advanceTimersByTimeAsync(0)
  expect(post).toHaveBeenCalledTimes(2)
  expect(post.mock.calls[1][0].settings['config/theme']).toBe('new')
  ack(post.mock.calls[1][0].requestId)
  await second
  expect(notify).toHaveBeenCalledTimes(1)
  expect(listeners.size).toBe(0)
})

it('skips identical queued snapshots only after success and retries them after failure', async () => {
  const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
  localStorage.setItem('config/theme', 'same')
  const first = saveDashboardSettingsToHost().catch((error: Error) => error)
  const retry = saveDashboardSettingsToHost()
  const duplicate = saveDashboardSettingsToHost()
  await vi.advanceTimersByTimeAsync(0)
  ack(post.mock.calls[0][0].requestId, false)
  expect(await first).toBeInstanceOf(Error)
  await vi.advanceTimersByTimeAsync(0)
  expect(post).toHaveBeenCalledTimes(2)
  ack(post.mock.calls[1][0].requestId)
  await Promise.all([retry, duplicate])
  expect(post).toHaveBeenCalledTimes(2)
  await saveDashboardSettingsToHost({ beforeReload: true })
  expect(post).toHaveBeenCalledTimes(2)
})

it('rejects on timeout and removes the listener', async () => {
  const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
  const result = saveDashboardSettingsToHost({ beforeReload: true }).catch((error: Error) => error)
  await vi.advanceTimersByTimeAsync(10001)
  expect(await result).toBeInstanceOf(Error)
  expect(listeners.size).toBe(0)
  expect(notify).toHaveBeenCalledTimes(1)
})
