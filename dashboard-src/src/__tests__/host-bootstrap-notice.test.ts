import { afterEach, expect, it, vi } from 'vitest'
import { makeHostSnapshot } from './hostFixture'
const { notification, addBackend, getBackendFromUrl } = vi.hoisted(() => ({
  notification: vi.fn(),
  addBackend: vi.fn(),
  getBackendFromUrl: vi.fn(() => ({
    protocol: 'http',
    host: 'browser.test',
    port: '9090',
    type: 'clash',
  })),
}))
vi.mock('@/helper/notification', () => ({ showNotification: notification }))
vi.mock('@/i18n', () => ({ i18n: { global: { te: () => false, t: (key: string) => key } } }))
vi.mock('@/store/setup', () => ({ addBackend }))
vi.mock('@/helper/utils', () => ({ getBackendFromUrl }))
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it('desktop notices use explicit severity, and bootstrap never rewrites a browser backend list', async () => {
  vi.resetModules()
  notification.mockClear()
  addBackend.mockClear()
  getBackendFromUrl.mockClear()
  let receive!: (event: MessageEvent) => void
  Object.defineProperty(window, 'chrome', {
    configurable: true,
    value: {
      webview: {
        postMessage: vi.fn(),
        addEventListener: (_: string, listener: typeof receive) => {
          receive = listener
        },
        removeEventListener: vi.fn(),
      },
    },
  })
  await import('@/hostBootstrap')
  receive(
    new MessageEvent('message', {
      data: { protocolVersion: 2, type: 'notice', message: 'text', severity: 'error' },
    }),
  )
  expect(notification).toHaveBeenCalledWith(
    expect.objectContaining({ content: 'text', type: 'alert-error' }),
  )
  receive(
    new MessageEvent('message', {
      data: { protocolVersion: 2, type: 'state', state: makeHostSnapshot() },
    }),
  )
  expect(addBackend).not.toHaveBeenCalled()
  expect(getBackendFromUrl).not.toHaveBeenCalled()
})
it('retains URL import for browser preview', async () => {
  vi.resetModules()
  getBackendFromUrl.mockClear()
  addBackend.mockClear()
  await import('@/hostBootstrap')
  expect(addBackend).toHaveBeenCalledWith(expect.objectContaining({ host: 'browser.test' }))
})
