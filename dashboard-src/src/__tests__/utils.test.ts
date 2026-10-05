import { beforeEach, describe, expect, it, vi } from 'vitest'
import { makeHostSnapshot } from './hostFixture'

describe('backend URL parsing', () => {
  beforeEach(() => {
    history.replaceState(null, '', '/')
    localStorage.clear()
    Object.defineProperty(window, 'chrome', {
      configurable: true,
      value: undefined,
    })
  })

  it('parses desktop query parameters into a clash backend shape', async () => {
    history.replaceState(
      null,
      '',
      '/?protocol=https&hostname=example.test&port=9443&secondaryPath=%2Fapi&secret=s3cr3t&label=Local&disableUpgradeCore=1&disableTunMode=1',
    )
    const { getBackendFromUrl } = await import('@/helper/utils')

    expect(getBackendFromUrl()).toMatchObject({
      type: 'clash',
      protocol: 'https',
      host: 'example.test',
      port: '9443',
      secondaryPath: '/api',
      password: 's3cr3t',
      label: 'Local',
      disableUpgradeCore: true,
      disableTunMode: true,
    })
  })

  it('ignores obsolete native backend URL parameters', async () => {
    history.replaceState(null, '', '/?type=singbox&http=1&hostname=box.local&port=9091')
    const { getBackendFromUrl } = await import('@/helper/utils')

    expect(getBackendFromUrl()).toBeNull()
  })

  it('parses backend parameters from hash URLs and formats backend URLs', async () => {
    history.replaceState(null, '', '/#/core?protocol=http&hostname=127.0.0.1&port=9090')
    const { getBackendFromUrl, getUrlFromBackend } = await import('@/helper/utils')

    expect(getBackendFromUrl()).toMatchObject({
      type: 'clash',
      protocol: 'http',
      host: '127.0.0.1',
      port: '9090',
      secondaryPath: '',
    })
    expect(
      getUrlFromBackend({
        protocol: 'http',
        host: '127.0.0.1',
        port: '9090',
        secondaryPath: '/ui',
      }),
    ).toBe('http://127.0.0.1:9090/ui')
  })
})

describe('dashboard settings storage', () => {
  beforeEach(() => {
    localStorage.clear()
    Object.defineProperty(window, 'chrome', {
      configurable: true,
      value: undefined,
    })
  })

  it('collects only config-prefixed localStorage entries', async () => {
    const { getDashboardSettingsFromStorage } = await import('@/helper/utils')

    localStorage.setItem('config/default-theme', '"light"')
    localStorage.setItem('config/proxy-sort-type', '"default"')
    localStorage.setItem('cache/proxy-icon', '{}')
    localStorage.setItem('setup/api-list', '[]')

    expect(getDashboardSettingsFromStorage()).toEqual({
      'config/default-theme': '"light"',
      'config/proxy-sort-type': '"default"',
    })
  })

  it('applies and clears only config-prefixed dashboard settings', async () => {
    const { applyDashboardSettingsToStorage, clearDashboardSettingsFromStorage } =
      await import('@/helper/utils')

    localStorage.setItem('setup/api-list', '[]')
    applyDashboardSettingsToStorage({
      'config/default-theme': '"dark"',
      'cache/proxy-icon': '{}',
      'config/not-a-string': false,
    })

    expect(localStorage.getItem('config/default-theme')).toBe('"dark"')
    expect(localStorage.getItem('cache/proxy-icon')).toBeNull()
    expect(localStorage.getItem('config/not-a-string')).toBeNull()

    clearDashboardSettingsFromStorage()
    expect(localStorage.getItem('config/default-theme')).toBeNull()
    expect(localStorage.getItem('setup/api-list')).toBe('[]')
  })

  it('posts config settings to the desktop host bridge', async () => {
    vi.resetModules()
    const listeners = new Set<(event: MessageEvent) => void>()
    const postMessage = vi.fn((message) => {
      queueMicrotask(() =>
        listeners.forEach((listener) =>
          listener(
            new MessageEvent('message', {
              data: {
                protocolVersion: 2,
                type: 'commandResult',
                requestId: message.requestId,
                state: makeHostSnapshot(),
                result: { status: 'completed', code: 'saved', saved: true },
              },
            }),
          ),
        ),
      )
    })
    Object.defineProperty(window, 'chrome', {
      configurable: true,
      value: {
        webview: {
          postMessage,
          addEventListener: (_type: string, listener: (event: MessageEvent) => void) =>
            listeners.add(listener),
          removeEventListener: (_type: string, listener: (event: MessageEvent) => void) =>
            listeners.delete(listener),
        },
      },
    })
    localStorage.setItem('config/default-theme', '"light"')
    localStorage.setItem('cache/proxy-icon', '{}')

    const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
    await saveDashboardSettingsToHost()

    expect(postMessage).toHaveBeenCalledWith({
      type: 'saveDashboardPreferences',
      protocolVersion: 2,
      requestId: expect.any(String),
      preferences: {
        'config/default-theme': '"light"',
      },
    })
  })
})
