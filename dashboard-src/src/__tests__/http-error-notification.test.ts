import axios, { AxiosError } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { applyHostRuntimeState } from '@/composables/hostBridge'
import { captureBackendSession } from '@/helper/backendSession'
import { markRequestErrorHandled, notifyRequestError, notifyRequestErrorForSession, runManualRequest } from '@/helper/requestError'
import { activeUuid, addBackend } from '@/store/setup'
import router from '@/router'
import '@/api/http'

const { showNotification } = vi.hoisted(() => ({ showNotification: vi.fn() }))
vi.mock('@/helper/notification', () => ({ showNotification }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: { value: false } }))

const rejectRequest = (url: string) => axios.get(url, {
  adapter: (config) => Promise.reject(new AxiosError('Server failed', 'ERR_BAD_RESPONSE', config, null,
    { config, data: { message: 'Bad request' }, headers: {}, status: 500, statusText: 'Error' })),
})

beforeEach(() => {
  showNotification.mockClear()
  addBackend({ type: 'clash', protocol: 'http', host: 'localhost', port: '9090', secondaryPath: '', password: 'old' }, { replaceExisting: true })
})

describe('request error ownership', () => {
  it('rejects background fetches without global notifications even for formerly non-blacklisted URLs', async () => {
    await expect(rejectRequest('/proxies')).rejects.toMatchObject({ response: { status: 500 } })
    await expect(rejectRequest('/configs')).rejects.toMatchObject({ response: { status: 500 } })
    expect(showNotification).not.toHaveBeenCalled()
  })

  it('reports a manual failure exactly once with response message', async () => {
    await runManualRequest(() => rejectRequest('/cache/dns/flush'))
    expect(showNotification).toHaveBeenCalledTimes(1)
    expect(showNotification).toHaveBeenCalledWith(expect.objectContaining({ type: 'alert-error', content: expect.stringContaining('Bad request') }))
  })

  it('does not report a stale manual failure or a marked 401 error', async () => {
    const session = captureBackendSession()
    applyHostRuntimeState({ processId: Math.random() })
    notifyRequestErrorForSession(new Error('old'), session)
    const error = new Error('handled')
    markRequestErrorHandled(error)
    notifyRequestError(error)
    expect(showNotification).not.toHaveBeenCalled()
  })

  it('does not treat an external absolute URL 401 as the active core credentials', async () => {
    const currentUuid = activeUuid.value
    const push = vi.spyOn(router, 'push')
    await expect(axios.get('https://external.example/profile', {
      adapter: (config) => Promise.reject(new AxiosError('Unauthorized', '401', config, null,
        { config, data: {}, headers: {}, status: 401, statusText: 'Unauthorized' })),
    })).rejects.toMatchObject({ response: { status: 401 } })
    expect(activeUuid.value).toBe(currentUuid)
    expect(push).not.toHaveBeenCalled()
    push.mockRestore()
    expect(showNotification).not.toHaveBeenCalled()
  })

  it('keeps cancellation silent', () => {
    notifyRequestError(new axios.CanceledError('Backend session changed'))
    expect(showNotification).not.toHaveBeenCalled()
  })
})
