import { afterEach, expect, it, vi } from 'vitest'
const { notification, addBackend, getBackendFromUrl } = vi.hoisted(() => ({ notification: vi.fn(), addBackend: vi.fn(), getBackendFromUrl: vi.fn(() => ({ protocol: 'http', host: 'browser.test', port: '9090', type: 'clash' })) }))
vi.mock('@/helper/notification', () => ({ showNotification: notification }))
vi.mock('@/store/setup', () => ({ addBackend, activeUuid: { value: 'previous' } }))
vi.mock('@/helper/utils', () => ({ getBackendFromUrl }))
afterEach(() => { Reflect.deleteProperty(window, 'chrome') })

it('shows notices without CorePage and does not reapply an unchanged backend', async () => {
  vi.resetModules()
  let receive: (event: MessageEvent) => void = () => {}
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: vi.fn(),
    addEventListener: (_: string, listener: typeof receive) => { receive = listener },
    removeEventListener: vi.fn(),
  } } })
  await import('@/hostBootstrap')
  expect(getBackendFromUrl).not.toHaveBeenCalled()
  expect(addBackend).not.toHaveBeenCalled()
  receive(new MessageEvent('message', { data: { type: 'notice', message: '内核已重启。' } }))
  receive(new MessageEvent('message', {
    data: { type: 'notice', message: '发现 Dashboard 新版本也可以是普通业务通知。' },
  }))
  expect(notification).toHaveBeenCalledTimes(2)
  const state = { apiUrl: 'http://localhost:9090', secret: 'test', coreType: 'mihomo', processId: 1 }
  receive(new MessageEvent('message', { data: { type: 'state', state } }))
  receive(new MessageEvent('message', { data: { type: 'state', state: { ...state, coreVersion: 'new' } } }))
  expect(addBackend).toHaveBeenCalledTimes(1)
  receive(new MessageEvent('message', { data: { type: 'state', state: { apiUrl: '' } } }))
  const { activeUuid } = await import('@/store/setup')
  expect(activeUuid.value).toBeNull()
})

it('retains URL backend import for browser preview', async () => {
  vi.resetModules(); getBackendFromUrl.mockClear(); addBackend.mockClear()
  await import('@/hostBootstrap')
  expect(getBackendFromUrl).toHaveBeenCalledOnce()
  expect(addBackend).toHaveBeenCalledWith(expect.objectContaining({ host: 'browser.test' }), { replaceExisting: false })
})
