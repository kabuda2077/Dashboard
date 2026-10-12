import { afterEach, expect, it, vi } from 'vitest'
import { startMockHost } from './hostFixture'
afterEach(async () => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
  const { default: axios } = await import('axios')
  axios.interceptors.request.clear()
  axios.interceptors.response.clear()
})
it.each([NaN, Infinity, -1])(
  'invalid explicit client timeout %s remains bounded',
  async (timeout) => {
    vi.resetModules()
    localStorage.clear()
    await startMockHost()
    await import('@/api/http')
    const { default: axios } = await import('axios')
    const response = await axios.get('/version', {
      timeout,
      adapter: async (config) => ({
        data: { version: 'fixture' },
        status: 200,
        statusText: 'OK',
        headers: {},
        config,
      }),
    })
    expect(response.config.timeout).toBe(30000)
  },
)

it('uses current credentials and removes inherited authorization when the secret is cleared', async () => {
  vi.resetModules()
  localStorage.clear()
  const host = await startMockHost()
  host.runtime({ secret: 'current-secret' })
  await import('@/api/http')
  const { default: axios } = await import('axios')
  const { nextTick } = await import('vue')
  const previous = axios.defaults.headers.common.Authorization
  axios.defaults.headers.common.Authorization = 'Bearer stale-default'
  const request = () =>
    axios.get('/version', {
      adapter: async (config) => ({ data: {}, status: 200, statusText: 'OK', headers: {}, config }),
    })
  try {
    expect((await request()).config.headers.get('Authorization')).toBe('Bearer current-secret')
    host.runtime({ secret: '' })
    await nextTick()
    expect((await request()).config.headers.has('Authorization')).toBe(false)
    host.runtime({ secret: 'replacement-secret' })
    await nextTick()
    expect((await request()).config.headers.get('Authorization')).toBe('Bearer replacement-secret')
  } finally {
    if (previous === undefined) delete axios.defaults.headers.common.Authorization
    else axios.defaults.headers.common.Authorization = previous
  }
})

it('bounds PTR queries and forwards their name, type and cancellation signal to the current API', async () => {
  vi.resetModules()
  localStorage.clear()
  await startMockHost()
  await import('@/api/http')
  const { default: axios } = await import('axios')
  const { queryDNSAPI } = await import('@/api/clash')
  const adapter = axios.defaults.adapter
  const caller = new AbortController()
  axios.defaults.adapter = async (config) => {
    expect(config.url).toBe('/dns/query')
    expect(config.params).toEqual({ name: '10.2.0.192.in-addr.arpa', type: 'PTR' })
    expect(config.timeout).toBe(5000)
    expect(config.signal?.aborted).toBe(false)
    return {
      data: { Answer: [{ type: 12, data: 'desktop.example.' }] },
      status: 200,
      statusText: 'OK',
      headers: {},
      config,
    }
  }
  try {
    expect(
      (await queryDNSAPI({ name: '10.2.0.192.in-addr.arpa', type: 'PTR' }, caller.signal)).data
        .Answer?.[0].data,
    ).toBe('desktop.example.')
    caller.abort()
    await expect(
      queryDNSAPI({ name: '10.2.0.192.in-addr.arpa', type: 'PTR' }, caller.signal),
    ).rejects.toSatisfy(axios.isCancel)
  } finally {
    axios.defaults.adapter = adapter
  }
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
