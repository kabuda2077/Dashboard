import { fetchSmartWeightsAPI } from '@/api/clash'
import { activeBackend, addBackend, updateBackend } from '@/store/setup'
import { initSmartWeights, smartOrderMap, smartWeightsMap } from '@/store/smart'
import axios from 'axios'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const smartApi = vi.hoisted(() => ({
  fetchSmartGroupWeightsAPI: vi.fn(),
  fetchSmartWeightsAPI: vi.fn(),
}))

vi.mock('@/assembly/proxies', () => smartApi)

const deferred = <T>() => {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, reject, resolve }
}

afterEach(() => vi.restoreAllMocks())

beforeEach(() => {
  vi.clearAllMocks()
  addBackend(
    {
      type: 'clash',
      protocol: 'http',
      host: 'localhost',
      port: '9090',
      secondaryPath: '',
      password: 'smart-test',
    },
    { replaceExisting: true },
  )
  smartWeightsMap.value = {}
  smartOrderMap.value = {}
})

it('lets only a 404 compatibility response resolve from the aggregate API', async () => {
  const get = vi.spyOn(axios, 'get').mockResolvedValue({ status: 404, data: {} })

  await fetchSmartWeightsAPI()

  const config = get.mock.calls[0][1]!
  expect(config.validateStatus?.(200)).toBe(true)
  expect(config.validateStatus?.(404)).toBe(true)
  expect(config.validateStatus?.(500)).toBe(false)
})

it('falls back to the deprecated per-group endpoint only when the aggregate endpoint is absent', async () => {
  smartApi.fetchSmartWeightsAPI.mockResolvedValue({ status: 404, data: {} })
  smartApi.fetchSmartGroupWeightsAPI.mockImplementation(async (group: string) => ({
    data: {
      weights:
        group === 'auto'
          ? [
              { Name: 'node-b', Rank: 'frequently' },
              { Name: 'node-a', Rank: 'occasionally' },
            ]
          : [],
    },
  }))

  await initSmartWeights(['auto', 'empty'])

  expect(smartApi.fetchSmartGroupWeightsAPI).toHaveBeenCalledTimes(2)
  expect(smartWeightsMap.value).toEqual({
    auto: { 'node-b': 'frequently', 'node-a': 'occasionally' },
  })
  expect(smartOrderMap.value).toEqual({ auto: { 'node-b': 0, 'node-a': 1 } })
})

it('does not reinterpret network or server failures as compatibility fallback', async () => {
  smartWeightsMap.value = { existing: { node: 'occasionally' } }
  smartOrderMap.value = { existing: { node: 0 } }
  smartApi.fetchSmartWeightsAPI.mockRejectedValueOnce(new Error('network unavailable'))

  await expect(initSmartWeights(['auto'])).rejects.toThrow('network unavailable')
  expect(smartApi.fetchSmartGroupWeightsAPI).not.toHaveBeenCalled()
  expect(smartWeightsMap.value).toEqual({ existing: { node: 'occasionally' } })

  smartApi.fetchSmartWeightsAPI.mockResolvedValueOnce({ status: 500, data: {} })
  await expect(initSmartWeights(['auto'])).rejects.toThrow(
    'Unexpected Smart weights response: HTTP 500',
  )
  expect(smartApi.fetchSmartGroupWeightsAPI).not.toHaveBeenCalled()
})

it('does not publish partial fallback data when a legacy request fails', async () => {
  smartWeightsMap.value = { existing: { node: 'frequently' } }
  smartApi.fetchSmartWeightsAPI.mockResolvedValue({ status: 404, data: {} })
  smartApi.fetchSmartGroupWeightsAPI.mockImplementation(async (group: string) => {
    if (group === 'broken') throw new Error('legacy endpoint failed')
    return { data: { weights: [{ Name: 'new-node', Rank: 'occasionally' }] } }
  })

  await expect(initSmartWeights(['working', 'broken'])).rejects.toThrow('legacy endpoint failed')
  expect(smartWeightsMap.value).toEqual({ existing: { node: 'frequently' } })
})

it('drops fallback results that complete after the backend session changes', async () => {
  const legacyResponse = deferred<{ data: { weights: { Name: string; Rank: string }[] } }>()
  smartWeightsMap.value = { current: { node: 'frequently' } }
  smartOrderMap.value = { current: { node: 0 } }
  smartApi.fetchSmartWeightsAPI.mockResolvedValue({ status: 404, data: {} })
  smartApi.fetchSmartGroupWeightsAPI.mockReturnValue(legacyResponse.promise)

  const loading = initSmartWeights(['auto'])
  await vi.waitFor(() => expect(smartApi.fetchSmartGroupWeightsAPI).toHaveBeenCalled())

  const backend = activeBackend.value!
  updateBackend(backend.uuid, { ...backend, password: 'next-session' })
  legacyResponse.resolve({ data: { weights: [{ Name: 'old-node', Rank: 'occasionally' }] } })
  await loading

  expect(smartWeightsMap.value).toEqual({ current: { node: 'frequently' } })
  expect(smartOrderMap.value).toEqual({ current: { node: 0 } })
})
