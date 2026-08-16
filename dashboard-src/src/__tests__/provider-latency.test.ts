import { fetchProxyProviderLatencyAPI } from '@/api/clash'
import axios from 'axios'
import { describe, expect, it, vi } from 'vitest'

vi.mock('axios', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: { delay: 42 }, status: 200 })),
  },
}))

describe('provider scoped latency endpoint', () => {
  it('targets a single node through the provider healthcheck route', async () => {
    await fetchProxyProviderLatencyAPI('Provider A', 'Node/B', 'https://example.test', 3000)

    expect(axios.get).toHaveBeenCalledWith(
      '/providers/proxies/Provider%20A/Node%2FB/healthcheck',
      {
        params: {
          url: 'https://example.test',
          timeout: 3000,
        },
      },
    )
  })
})
