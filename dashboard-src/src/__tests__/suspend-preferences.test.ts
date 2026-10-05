import { afterEach, beforeEach, expect, it, vi } from 'vitest'

vi.mock('@/helper/requestError', () => ({ notifyRequestError: vi.fn() }))
let listeners: Set<(event: MessageEvent) => void>
let post: ReturnType<typeof vi.fn>
let dispose: (() => void) | undefined
const message = (data: Record<string, unknown>) => [...listeners].forEach((fn) => fn(new MessageEvent('message', { data })))
const saveAck = (requestId: string, success = true) => message({ type: 'dashboardSettingsSaved', requestId, success })
const posts = (type: string) => post.mock.calls.map(([value]) => value).filter((value) => value.type === type)

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  localStorage.clear()
  listeners = new Set()
  post = vi.fn()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: post,
    addEventListener: (_: string, fn: (event: MessageEvent) => void) => listeners.add(fn),
    removeEventListener: (_: string, fn: (event: MessageEvent) => void) => listeners.delete(fn),
  } } })
})
afterEach(() => {
  dispose?.()
  dispose = undefined
  vi.clearAllTimers()
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})
const install = async () => {
  const sync = await import('@/helper/dashboardSettingsSync')
  dispose = sync.installDashboardSettingsSync()
  return sync
}

it('flushes a pending debounce immediately and authorizes suspension only after the disk ACK', async () => {
  await install()
  localStorage.setItem('config/theme', 'light')
  message({ type: 'flushDashboardPreferences', requestId: 'flush-1' })
  await vi.advanceTimersByTimeAsync(0)
  expect(posts('saveDashboardSettings')).toHaveLength(1)
  expect(posts('dashboardPreferencesFlushed')).toHaveLength(0)
  saveAck('unrelated-document')
  await vi.advanceTimersByTimeAsync(0)
  expect(posts('dashboardPreferencesFlushed')).toHaveLength(0)
  const save = posts('saveDashboardSettings')[0]
  expect(save.settings['config/theme']).toBe('light')
  saveAck(save.requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(posts('dashboardPreferencesFlushed')).toEqual([{ type: 'dashboardPreferencesFlushed', requestId: 'flush-1', success: true }])
})

it('includes edits made while a previous save is in flight', async () => {
  await install()
  localStorage.setItem('config/theme', 'old')
  message({ type: 'flushDashboardPreferences', requestId: 'flush-2' })
  await vi.advanceTimersByTimeAsync(0)
  localStorage.setItem('config/theme', 'new')
  saveAck(posts('saveDashboardSettings')[0].requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(posts('dashboardPreferencesFlushed')).toHaveLength(0)
  expect(posts('saveDashboardSettings')[1].settings['config/theme']).toBe('new')
  saveAck(posts('saveDashboardSettings')[1].requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(posts('dashboardPreferencesFlushed')[0].success).toBe(true)
})

it.each(['failure', 'timeout'])('does not authorize suspension after a save %s and allows a later retry', async (kind) => {
  await install()
  message({ type: 'flushDashboardPreferences', requestId: 'failed' })
  await vi.advanceTimersByTimeAsync(0)
  if (kind === 'failure') saveAck(posts('saveDashboardSettings')[0].requestId, false)
  else await vi.advanceTimersByTimeAsync(10001)
  await vi.advanceTimersByTimeAsync(0)
  expect(posts('dashboardPreferencesFlushed')).toEqual([{ type: 'dashboardPreferencesFlushed', requestId: 'failed', success: false }])
  message({ type: 'flushDashboardPreferences', requestId: 'retry' })
  await vi.advanceTimersByTimeAsync(0)
  saveAck(posts('saveDashboardSettings')[1].requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(posts('dashboardPreferencesFlushed')[1]).toMatchObject({ requestId: 'retry', success: true })
})

it('does not send a late flush ACK after the document has been disposed', async () => {
  await install()
  message({ type: 'flushDashboardPreferences', requestId: 'old-document' })
  await vi.advanceTimersByTimeAsync(0)
  dispose?.()
  saveAck(posts('saveDashboardSettings')[0].requestId)
  await vi.advanceTimersByTimeAsync(0)
  expect(posts('dashboardPreferencesFlushed')).toHaveLength(0)
  expect(listeners.size).toBe(0)
})
