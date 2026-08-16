import type { Backend } from '@/types'
import { beforeEach, describe, expect, it, vi } from 'vitest'

describe('setup backend migration', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    vi.resetModules()
  })

  it('adds missing clash type and splits legacy singboxChannel into a native backend', async () => {
    localStorage.setItem(
      'setup/api-list',
      JSON.stringify([
        {
          protocol: 'http',
          host: '127.0.0.1',
          port: '9090',
          secondaryPath: '',
          password: 'clash-secret',
          uuid: 'legacy-clash',
          label: 'Local',
          singboxChannel: {
            protocol: 'https',
            host: '127.0.0.1',
            port: '9443',
            secret: 'singbox-secret',
          },
        },
      ]),
    )

    const { backendList } = await import('@/store/setup')

    expect(backendList.value).toHaveLength(2)
    expect(backendList.value[0]).toMatchObject({
      type: 'clash',
      uuid: 'legacy-clash',
      password: 'clash-secret',
    })
    expect(backendList.value[1]).toMatchObject({
      type: 'singbox',
      protocol: 'https',
      host: '127.0.0.1',
      port: '9443',
      secondaryPath: '',
      password: 'singbox-secret',
      label: 'Local (sing-box)',
    })
  })

  it('replaces duplicate endpoints instead of adding another backend', async () => {
    const { activeUuid, addBackend, backendList } = await import('@/store/setup')
    const backend: Omit<Backend, 'uuid'> = {
      type: 'clash',
      protocol: 'http',
      host: '127.0.0.1',
      port: '9090',
      secondaryPath: '',
      password: '',
    }

    addBackend(backend)
    const firstUuid = activeUuid.value
    addBackend({ ...backend, label: 'Updated' })

    expect(backendList.value).toHaveLength(1)
    expect(activeUuid.value).toBe(firstUuid)
    expect(backendList.value[0]?.label).toBe('Updated')
  })

  it('drops stale fields when replacing the desktop injected backend', async () => {
    const { addBackend, backendList } = await import('@/store/setup')
    const backend: Omit<Backend, 'uuid'> = {
      type: 'clash',
      protocol: 'http',
      host: '127.0.0.1',
      port: '9090',
      secondaryPath: '',
      password: '',
      readOnlyTunEnabled: false,
      disableTunMode: true,
    }

    addBackend(backend, { replaceExisting: true })
    addBackend({
      type: 'clash',
      protocol: 'http',
      host: '127.0.0.1',
      port: '9090',
      secondaryPath: '',
      password: '',
      label: '本机内核',
    }, { replaceExisting: true })

    expect(backendList.value).toHaveLength(1)
    expect(backendList.value[0]).toMatchObject({
      label: '本机内核',
    })
    expect(backendList.value[0]?.readOnlyTunEnabled).toBeUndefined()
    expect(backendList.value[0]?.disableTunMode).toBeUndefined()
  })
})
