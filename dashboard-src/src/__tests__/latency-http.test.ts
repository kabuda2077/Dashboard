import { getLatencyFromUrlAPI } from '@/api/latency'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const fetchMock = vi.fn()
beforeEach(() => {
  vi.useFakeTimers()
  vi.stubGlobal('fetch', fetchMock.mockReset())
})
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})
it('times response arrival with no-cors and unique cache keys, then cancels the body', async () => {
  const cancel = vi.fn().mockResolvedValue(undefined)
  fetchMock.mockResolvedValue({ type: 'opaque', body: { cancel } })
  expect(await getLatencyFromUrlAPI('https://example.test/check')).toBeGreaterThan(0)
  expect(await getLatencyFromUrlAPI('https://example.test/check')).toBeGreaterThan(0)
  const [[first, options], [second]] = fetchMock.mock.calls
  expect(first).not.toBe(second)
  expect(options).toMatchObject({
    method: 'GET',
    mode: 'no-cors',
    credentials: 'omit',
    cache: 'no-store',
    redirect: 'follow',
    referrerPolicy: 'no-referrer',
  })
  expect(options.signal.aborted).toBe(true)
  expect(cancel).toHaveBeenCalledTimes(2)
  expect(vi.getTimerCount()).toBe(0)
})
it.each(['timeout', 'session'] as const)(
  'aborts %s requests and retains a failed sample',
  async (kind) => {
    fetchMock.mockImplementation(
      (_: string, { signal }: RequestInit) =>
        new Promise((_, reject) => {
          signal!.addEventListener(
            'abort',
            () => reject(new DOMException('Aborted', 'AbortError')),
            { once: true },
          )
        }),
    )
    const controller = new AbortController()
    const pending = getLatencyFromUrlAPI('https://example.test/check', controller.signal)
    if (kind === 'session') controller.abort()
    else await vi.advanceTimersByTimeAsync(5000)
    expect(await pending).toBe(0)
    expect(vi.getTimerCount()).toBe(0)
  },
)
