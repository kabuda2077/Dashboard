import type { Backend } from '@/types'
import { beforeEach, describe, expect, it, vi } from 'vitest'

describe('setup backend migration', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    vi.resetModules()
  })

  it('adds the clash type and discards the legacy native channel', async () => {
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

    expect(backendList.value).toHaveLength(1)
    expect(backendList.value[0]).toMatchObject({
      type: 'clash',
      uuid: 'legacy-clash',
      password: 'clash-secret',
    })
  })

  it('removes stored native backends without treating port 9091 as Clash API', async () => {
    localStorage.setItem(
      'setup/api-list',
      JSON.stringify([
        {
          type: 'singbox',
          protocol: 'http',
          host: '127.0.0.1',
          port: '9091',
          secondaryPath: '',
          password: '',
          uuid: 'native',
        },
      ]),
    )
    localStorage.setItem('setup/active-uuid', 'native')

    const { activeUuid, backendList } = await import('@/store/setup')

    expect(backendList.value).toEqual([])
    expect(activeUuid.value).toBe('')
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
