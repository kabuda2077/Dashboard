import { shouldIgnoreErrorNotification } from '@/api/http'
import axios, { AxiosError } from 'axios'
import { describe, expect, it } from 'vitest'

describe('HTTP error notifications', () => {
  it('keeps background version probes silent', () => {
    expect(shouldIgnoreErrorNotification('/version')).toBe(true)
  })

  it('still reports errors from user-facing API requests', () => {
    expect(shouldIgnoreErrorNotification('/proxies')).toBe(false)
  })

  it('keeps transient config network failures silent', () => {
    expect(
      shouldIgnoreErrorNotification('/configs', {
        isNetworkError: true,
      }),
    ).toBe(true)
  })

  it('does not hide a real config response error', () => {
    expect(
      shouldIgnoreErrorNotification('/configs', {
        isNetworkError: false,
      }),
    ).toBe(false)
  })

  it('keeps silent config network failures rejected', async () => {
    const request = axios.get('/configs', {
      adapter: (config) =>
        Promise.reject(new AxiosError('Network Error', AxiosError.ERR_NETWORK, config)),
    })

    await expect(request).rejects.toMatchObject({
      code: AxiosError.ERR_NETWORK,
      message: 'Network Error',
    })
  })
})
