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

    expect(backend.capabilities.value).toMatchObject({
      proxies: true,
      rules: true,
      providers: true,
      upgrade: true,
      tools: false,
    })
  })

})
