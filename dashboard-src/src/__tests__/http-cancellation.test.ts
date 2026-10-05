import { afterEach, expect, it, vi } from 'vitest'
import { startMockHost } from './hostFixture'
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it.each([NaN, Infinity, -1])('invalid explicit client timeout %s remains bounded', async (timeout) => {
  vi.resetModules()
  localStorage.clear()
  await startMockHost()
  await import('@/api/http')
  const { default: axios } = await import('axios')
  const response = await axios.get('/version', {
    timeout,
    adapter: async (config) => ({ data: { version: 'fixture' }, status: 200, statusText: 'OK', headers: {}, config }),
  })
  expect(response.config.timeout).toBe(30000)
})

it.each(['epoch', 'readiness', 'caller'])(
  'in-flight API requests have a client budget and cancellation (%s)',
  async (reason) => {
    vi.resetModules()
    localStorage.clear()
    const host = await startMockHost()
    await import('@/api/http')
    const { default: axios, CanceledError } = await import('axios')
    const caller = new AbortController()
    let observed: { timeout?: number; signal?: { aborted: boolean } } | undefined
    const pending = axios
      .get('/proxies', {
        signal: caller.signal,
        adapter: (config) =>
          new Promise((_, reject) => {
            observed = config as typeof observed
            config.signal!.addEventListener!(
              'abort',
              () => reject(new CanceledError('cancelled')),
              { once: true },
            )
          }),
      })
      .catch((error) => error)
    await vi.waitFor(() => expect(observed).toBeDefined())
    expect(observed!.timeout).toBe(30000)
    if (reason === 'epoch') host.runtime({ runtimeEpoch: 2 })
    else if (reason === 'readiness') host.runtime({ apiStatus: 'unauthorized' })
    else caller.abort()
    expect(axios.isCancel(await pending)).toBe(true)
    expect(observed!.signal!.aborted).toBe(true)
  },
)
