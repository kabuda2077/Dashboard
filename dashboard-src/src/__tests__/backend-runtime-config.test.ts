import type { Config } from '@/types'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'

const getConfigsAPIMock = vi.fn()
const patchConfigsAPIMock = vi.fn()

vi.mock('@/api/clash', () => ({
  getConfigsAPI: getConfigsAPIMock,
  patchConfigsAPI: patchConfigsAPIMock,
}))

const deferred = <T>() => {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, reject, resolve }
}

const config = (allowLan: boolean): Config => ({
  port: 0,
  'socks-port': 0,
  'redir-port': 0,
  'tproxy-port': 0,
  'mixed-port': 7890,
  'allow-lan': allowLan,
  'bind-address': '',
  mode: 'rule',
  'mode-list': [],
  modes: [],
  'log-level': 'info',
  ipv6: false,
  tun: {
    enable: allowLan,
  },
})

describe('backend runtime config switching', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    getConfigsAPIMock.mockReset()
    patchConfigsAPIMock.mockReset()
    vi.resetModules()
  })

  it('loads the new core immediately and ignores the old pending response', async () => {
    const oldRequest = deferred<{ data: Config }>()
    const newRequest = deferred<{ data: Config }>()
    getConfigsAPIMock
      .mockImplementationOnce(() => oldRequest.promise)
      .mockImplementationOnce(() => newRequest.promise)

    const setup = await import('@/store/setup')
    setup.backendList.value = [
      {
        type: 'clash',
        protocol: 'http',
        host: '127.0.0.1',
        port: '9090',
        secondaryPath: '',
        password: '',
        uuid: 'desktop-core',
      },
    ]
    setup.activeUuid.value = 'desktop-core'

    const { HOST_BACKEND_UPDATED_EVENT } = await import('@/constant/hostEvents')
    const { useBackendRuntimeConfig } = await import('@/composables/useBackendRuntimeConfig')
    const scope = effectScope()
    scope.run(() => useBackendRuntimeConfig())

    await vi.waitFor(() => expect(getConfigsAPIMock).toHaveBeenCalledTimes(1))
    window.dispatchEvent(new CustomEvent(HOST_BACKEND_UPDATED_EVENT))
    await vi.waitFor(() => expect(getConfigsAPIMock).toHaveBeenCalledTimes(2))

    newRequest.resolve({ data: config(true) })
    const runtimeConfig = await import('@/assembly/config')
    await vi.waitFor(() => expect(runtimeConfig.configsLoaded.value).toBe(true))
    expect(runtimeConfig.configs.value['allow-lan']).toBe(true)

    oldRequest.resolve({ data: config(false) })
    await oldRequest.promise
    await Promise.resolve()
    expect(runtimeConfig.configs.value['allow-lan']).toBe(true)
    expect(runtimeConfig.configsLoadedBackendUuid.value).toBe('desktop-core')

    scope.stop()
  })
})
