import { describe, expect, it } from 'vitest'

describe('host bridge incremental messages', () => {
  it('merges runtime state without replacing the full host state', async () => {
    const { applyHostRuntimeState, applyHostState, hostState } = await import(
      '@/composables/hostBridge'
    )

    applyHostState({
      coreType: 'mihomo',
      apiUrl: 'http://127.0.0.1:9090',
      logText: 'existing',
    })
    applyHostRuntimeState({
      isRunning: true,
      processId: 42,
      coreTitle: 'Mihomo Core',
      coreVersion: 'v1.2.3',
      canUpgradeCore: true,
      isCoreUpgrading: false,
      isCoreSwitching: false,
      isWindowMaximized: true,
    })

    expect(hostState.value).toMatchObject({
      coreType: 'mihomo',
      apiUrl: 'http://127.0.0.1:9090',
      logText: 'existing',
      isRunning: true,
      processId: 42,
      coreVersion: 'v1.2.3',
    })
  })

  it('updates icon cache independently from full state', async () => {
    const { applyHostIconCache, hostIconCache } = await import('@/composables/hostBridge')

    applyHostIconCache({
      'https://example.test/icon.svg': 'http://127.0.0.1/icon.svg',
    })

    expect(hostIconCache.value).toEqual({
      'https://example.test/icon.svg': 'http://127.0.0.1/icon.svg',
    })
  })
})

describe('router lazy loading', () => {
  it('keeps secondary pages lazy while core pages stay eager', async () => {
    const { default: router } = await import('@/router')
    const coreRoute = router.getRoutes().find((route) => route.name === 'core')
    const logsRoute = router.getRoutes().find((route) => route.name === 'logs')
    const setupRoute = router.getRoutes().find((route) => route.name === 'setup')

    expect(typeof coreRoute?.components?.default).toBe('object')
    expect(typeof logsRoute?.components?.default).toBe('function')
    expect(typeof setupRoute?.components?.default).toBe('function')
  })
})
