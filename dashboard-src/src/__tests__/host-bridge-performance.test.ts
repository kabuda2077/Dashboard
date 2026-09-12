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
      appVersion: '1.2.0',
      latestAppVersion: '1.3.0',
      appUpdateAvailable: true,
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
      appVersion: '1.2.0',
      latestAppVersion: '1.3.0',
      appUpdateAvailable: true,
    })
  })

  it('refreshes the backend update event when the host version arrives asynchronously', async () => {
    const { HOST_BACKEND_UPDATED_EVENT } = await import('@/constant/hostEvents')
    const { applyHostRuntimeState, hostWindow } = await import('@/composables/hostBridge')
    const listener = vi.fn()
    window.addEventListener(HOST_BACKEND_UPDATED_EVENT, listener)

    hostWindow.__mihomoHostCoreVersion = ''
    applyHostRuntimeState({ coreVersion: 'sing-box 1.2.3' })

    expect(hostWindow.__mihomoHostCoreVersion).toBe('sing-box 1.2.3')
    expect(listener).toHaveBeenCalledTimes(1)
    window.removeEventListener(HOST_BACKEND_UPDATED_EVENT, listener)
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
    const routes = new Map(router.getRoutes().map((route) => [route.name, route]))
    const homeRoute = router.getRoutes().find((route) => route.path === '/')

    expect(typeof homeRoute?.components?.default, 'homepage').toBe('object')

    expect(typeof routes.get('core')?.components?.default, 'core').toBe('object')

    for (const routeName of ['proxies', 'overview', 'connections', 'logs', 'rules', 'setup']) {
      expect(typeof routes.get(routeName)?.components?.default, routeName).toBe('function')
    }

    const { renderRoutes } = await import('@/helper')
    expect(renderRoutes.value).toEqual([
      'core',
      'proxies',
      'connections',
      'overview',
      'logs',
      'rules',
    ])
  })
})
