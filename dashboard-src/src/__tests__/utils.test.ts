import { beforeEach, describe, expect, it, vi } from 'vitest'

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
      '/?https=1&hostname=example.test&port=9443&secondaryPath=%2Fapi&secret=s3cr3t&label=Local&disableUpgradeCore=1&disableTunMode=tun',
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

    expect(getBackendFromUrl()).toMatchObject({
      type: 'clash',
      protocol: 'http',
      host: 'box.local',
      port: '9091',
    })
  })

  it('parses backend parameters from hash URLs and formats backend URLs', async () => {
    history.replaceState(null, '', '/#/core?http=1&hostname=127.0.0.1&port=9090')
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
    const { applyDashboardSettingsToStorage, clearDashboardSettingsFromStorage } = await import(
      '@/helper/utils'
    )

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
      queueMicrotask(() => listeners.forEach((listener) => listener(new MessageEvent('message', {
        data: { type: 'dashboardSettingsSaved', requestId: message.requestId, success: true },
      }))))
    })
    Object.defineProperty(window, 'chrome', {
      configurable: true,
      value: {
        webview: {
          postMessage,
          addEventListener: (_type: string, listener: (event: MessageEvent) => void) => listeners.add(listener),
          removeEventListener: (_type: string, listener: (event: MessageEvent) => void) => listeners.delete(listener),
        },
      },
    })
    localStorage.setItem('config/default-theme', '"light"')
    localStorage.setItem('cache/proxy-icon', '{}')

    const { saveDashboardSettingsToHost } = await import('@/helper/dashboardSettingsSync')
    await saveDashboardSettingsToHost()

    expect(postMessage).toHaveBeenCalledWith({
      type: 'saveDashboardSettings',
      requestId: expect.any(String),
      settings: {
        'config/default-theme': '"light"',
      },
    })
  })
})

describe('proxy scrolling', () => {
  it('uses layout offsets when deciding whether a reordered card is visible', async () => {
    const { PROXIES_PARENT_CLASS, scrollIntoCenter } = await import('@/helper/utils')
    const parent = document.createElement('div')
    const card = document.createElement('div')
    const scrollTo = vi.fn()

    parent.classList.add(PROXIES_PARENT_CLASS)
    parent.append(card)
    Object.defineProperties(parent, {
      scrollHeight: { configurable: true, value: 1000 },
      clientHeight: { configurable: true, value: 100 },
      offsetTop: { configurable: true, value: 0 },
      scrollTop: { configurable: true, value: 100 },
      scrollTo: { configurable: true, value: scrollTo },
    })
    Object.defineProperties(card, {
      clientHeight: { configurable: true, value: 20 },
      offsetTop: { configurable: true, value: 120 },
    })

    scrollIntoCenter(card)
    expect(scrollTo).not.toHaveBeenCalled()

    Object.defineProperty(card, 'offsetTop', { configurable: true, value: 300 })
    scrollIntoCenter(card)
    expect(scrollTo).toHaveBeenCalledWith({ top: 260, behavior: 'smooth' })
  })
})
