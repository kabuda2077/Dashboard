import { fetchProxyProviderLatencyAPI } from '@/api/clash'
import axios from 'axios'
import { describe, expect, it, vi } from 'vitest'

vi.mock('axios', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: { delay: 42 }, status: 200 })),
  },
}))

describe('provider scoped latency endpoint', () => {
  it.each([NaN, Infinity, -1, 0, 1e12])('keeps a finite client deadline for invalid or excessive timeout %s', async (timeout) => {
    await fetchProxyProviderLatencyAPI('provider', 'node', 'https://example.test', timeout)
    const options = vi.mocked(axios.get).mock.lastCall![1]!
    expect(Number.isFinite(options.timeout)).toBe(true)
    expect(options.timeout).toBeGreaterThan(0)
    expect(options.timeout).toBeLessThanOrEqual(605000)
  })
  it('targets a single node through the provider healthcheck route', async () => {
    await fetchProxyProviderLatencyAPI('Provider A', 'Node/B', 'https://example.test', 3000)

    expect(axios.get).toHaveBeenCalledWith(
      '/providers/proxies/Provider%20A/Node%2FB/healthcheck',
      {
        params: {
          url: 'https://example.test',
          timeout: 3000,
        },
        timeout: 8000,
      },
    )
  })
})
