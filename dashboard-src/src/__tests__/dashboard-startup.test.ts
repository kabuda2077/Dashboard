import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { restoreDashboardSettings, startDashboard } from '@/helper/dashboardStartup'

let receive: (event: MessageEvent) => void
let post: ReturnType<typeof vi.fn>
let remove: ReturnType<typeof vi.fn>
beforeEach(() => {
  vi.useFakeTimers()
  localStorage.clear()
  remove = vi.fn()
  post = vi.fn()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: post,
    addEventListener: (_: string, listener: typeof receive) => { receive = listener },
    removeEventListener: remove,
  } } })
})
afterEach(() => { vi.useRealTimers(); Reflect.deleteProperty(window, 'chrome') })
const reply = (settings: unknown, id = post.mock.lastCall?.[0].requestId) => receive(new MessageEvent('message', {
  data: { type: 'dashboardSettingsSnapshot', requestId: id, settings },
}))

it('restores before loading stores and requests a fresh snapshot on each document startup', async () => {
  const load = vi.fn(async () => expect(localStorage.getItem('config/theme')).toBe('new'))
  const first = startDashboard(load)
  expect(load).not.toHaveBeenCalled()
  reply({ 'config/theme': 'old' }, 'obsolete')
  expect(load).not.toHaveBeenCalled()
  reply({ 'config/theme': 'new' })
  await first
  expect(load).toHaveBeenCalledOnce()
  const next = restoreDashboardSettings()
  reply({ 'config/theme': 'latest' })
  await next
  expect(localStorage.getItem('config/theme')).toBe('latest')
  expect(post).toHaveBeenCalledTimes(2)
  expect(remove).toHaveBeenCalledTimes(2)
})

it.each([null, undefined, {}])('distinguishes absent legacy preferences from explicit empty snapshot: %j', async (snapshot) => {
  localStorage.setItem('config/theme', 'legacy')
  localStorage.setItem('setup/api-list', 'preserved')
  const result = restoreDashboardSettings()
  reply(snapshot)
  await result
  expect(localStorage.getItem('config/theme')).toBe(snapshot == null ? 'legacy' : null)
  expect(localStorage.getItem('setup/api-list')).toBe('preserved')
})

it('boots a fresh desktop when the 1.3.0 host omits the null settings field', async () => {
  document.body.innerHTML = '<div id="app"></div>'
  const load = vi.fn(async () => {})
  const result = startDashboard(load)
  // Use the actual 1.3.0 wire shape, not a mock that inserts settings:null.
  receive(new MessageEvent('message', {
    data: { type: 'dashboardSettingsSnapshot', requestId: post.mock.lastCall?.[0].requestId },
  }))
  await result
  expect(load).toHaveBeenCalledOnce()
  expect(document.querySelector('button')).toBeNull()
  expect(localStorage.length).toBe(0)
})

it('fails visibly without loading stores, and retry can recover', async () => {
  document.body.innerHTML = '<div id="app"></div>'
  const load = vi.fn(async () => {})
  const result = startDashboard(load)
  await vi.advanceTimersByTimeAsync(10001)
  await result
  expect(load).not.toHaveBeenCalled()
  expect(remove).toHaveBeenCalledOnce()
  document.querySelector('button')!.click()
  reply({})
  await vi.advanceTimersByTimeAsync(0)
  expect(load).toHaveBeenCalledOnce()
})

it('starts normally without a desktop host', async () => {
  Reflect.deleteProperty(window, 'chrome')
  const load = vi.fn(async () => {})
  await startDashboard(load)
  expect(load).toHaveBeenCalledOnce()
})

it.each([
  { 'config/theme': 'new', 'setup/api-list': 'bad' },
  { 'config/theme': false },
  [], false, 0, 'invalid',
])('rejects malformed snapshots before mutating storage: %j', async (snapshot) => {
  localStorage.setItem('config/theme', 'kept')
  const result = restoreDashboardSettings().catch((error: Error) => error)
  reply(snapshot)
  expect(await result).toBeInstanceOf(Error)
  expect(localStorage.getItem('config/theme')).toBe('kept')
})
