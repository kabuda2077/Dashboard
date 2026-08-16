import type { Backend } from '@/types'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const clashBackend: Backend = {
  type: 'clash',
  protocol: 'http',
  host: '127.0.0.1',
  port: '9090',
  secondaryPath: '',
  password: '',
  uuid: 'clash',
}

const singboxBackend: Backend = {
  type: 'singbox',
  protocol: 'http',
  host: '127.0.0.1',
  port: '9443',
  secondaryPath: '',
  password: '',
  uuid: 'singbox',
}

describe('assembly backend routing', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    vi.resetModules()
  })

  it('enables clash-only capabilities for clash backends', async () => {
    const setup = await import('@/store/setup')
    const backend = await import('@/assembly/backend')
    setup.backendList.value = [clashBackend]
    setup.activeUuid.value = clashBackend.uuid

    expect(backend.isSingboxBackend.value).toBe(false)
    expect(backend.hasClashChannel.value).toBe(true)
    expect(backend.hasSingboxChannel.value).toBe(false)
    expect(backend.capabilities.value).toMatchObject({
      proxies: true,
      rules: true,
      providers: true,
      upgrade: true,
      tools: false,
    })
  })

  it('routes singbox native backends away from clash-only capabilities', async () => {
    const setup = await import('@/store/setup')
    const backend = await import('@/assembly/backend')
    setup.backendList.value = [singboxBackend]
    setup.activeUuid.value = singboxBackend.uuid

    expect(backend.isSingboxBackend.value).toBe(true)
    expect(backend.hasClashChannel.value).toBe(false)
    expect(backend.hasSingboxChannel.value).toBe(true)
    expect(backend.capabilities.value).toMatchObject({
      proxies: true,
      connections: true,
      rules: false,
      providers: false,
      upgrade: false,
    })
  })
})
