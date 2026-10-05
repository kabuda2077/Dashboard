import axios, { AxiosError } from 'axios'
import { afterEach, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { startMockHost } from './hostFixture'
const { push, notification } = vi.hoisted(() => ({ push: vi.fn(), notification: vi.fn() }))
vi.mock('@/router', () => ({ default: { push } }))
vi.mock('@/helper/notification', () => ({ showNotification: notification }))
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it('current desktop 401 opens Core without clearing profile identity; acknowledged credentials establish a new session', async () => {
  vi.resetModules()
  const host = await startMockHost({ secret: 'old' })
  await import('@/api/http')
  const setup = await import('@/store/setup')
  const session = await import('@/helper/backendSession')
  const unauthorized = () =>
    axios.get('/configs', {
      adapter: (config) =>
        Promise.reject(
          new AxiosError('Unauthorized', '401', config, null, {
            config,
            data: {},
            headers: {},
            status: 401,
            statusText: 'Unauthorized',
          }),
        ),
    })
  await expect(unauthorized()).rejects.toBeInstanceOf(AxiosError)
  await nextTick()
  await vi.waitFor(() => expect(push).toHaveBeenCalledWith(expect.objectContaining({ name: 'core' })))
  expect(notification).toHaveBeenCalledOnce()
  expect(setup.activeUuid.value).toBe('desktop:mihomo')
  expect(session.backendSessionReady.value).toBe(false)
  host.runtime({ secret: 'new', runtimeEpoch: 2, apiStatus: 'ready' })
  expect(session.backendSessionReady.value).toBe(true)
  expect(setup.activeBackend.value?.password).toBe('new')
})
