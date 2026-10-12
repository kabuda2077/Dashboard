import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'

// Stub page bodies and sidebar extras; keep the production router, navigation list and sidebar real.
vi.mock('@/views/CorePage.vue', () => ({ default: { render: () => null } }))
vi.mock('@/views/HomePage.vue', () => ({ default: { render: () => null } }))
vi.mock('@/views/SetupPage.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/sidebar/CommonCtrl.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/sidebar/OverviewCarousel.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/sidebar/SidebarButtons.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/sidebar/VerticalInfos.vue', () => ({ default: { render: () => null } }))

const pages = ['core', 'proxies', 'connections', 'overview', 'logs', 'rules']
let app: App | undefined
let root: HTMLElement | undefined

beforeEach(() => {
  vi.resetModules()
  localStorage.clear()
  sessionStorage.clear()
  Reflect.deleteProperty(window, 'chrome')
})
afterEach(() => {
  app?.unmount()
  root?.remove()
  app = undefined
  root = undefined
  Reflect.deleteProperty(window, 'chrome')
})

describe('production product routes and navigation', () => {
  it.each([null, '2', '3'])('defaults to one sidebar chart while preserving saved rows (%s)', async (saved) => {
    const key = 'config/number-of-charts-in-sidebar'
    if (saved !== null) localStorage.setItem(key, saved)
    const { numberOfChartsInSidebar } = await import('@/store/settings')
    expect(numberOfChartsInSidebar.value).toBe(saved === null ? 1 : Number(saved))
    expect(localStorage.getItem(key)).toBe(saved)
  })

  it('organizes sidebar statistics and formats low speed without changing live counters', async () => {
    const connections = await import('@/store/connections')
    const overview = await import('@/store/overview')
    const { sidebarStatItems, sidebarStatGrid } = await import('@/helper/sidebar-stats')
    connections.activeConnectionCount.value = 7
    connections.downloadTotal.value = 1200
    connections.uploadTotal.value = 3000
    overview.downloadSpeed.value = 500
    overview.uploadSpeed.value = 1500
    overview.memory.value = 2048
    expect(sidebarStatItems.value.map((item) => item.key)).toEqual(['connections', 'download', 'upload', 'memory'])
    expect(sidebarStatItems.value[0].value).toBe(7)
    expect(sidebarStatItems.value[1]).toMatchObject({ value: '0.5', unit: 'kB/s', total: '1.2 kB' })
    expect(sidebarStatItems.value[2]).toMatchObject({ value: '1.5', unit: 'kB/s', total: '3 kB' })
    expect(sidebarStatGrid.value.map((item) => item.key)).toEqual(['connections', 'memory', 'download', 'upload', 'dlSpeed', 'ulSpeed'])
    overview.downloadSpeed.value = Number.NaN
    expect(sidebarStatItems.value[1]).toMatchObject({ value: '0.0', unit: 'kB/s' })
    expect(connections.activeConnectionCount.value).toBe(7)
  })

  it.each([true, false])('keeps approved page entries and setup boundary (desktop=%s)', async (desktop) => {
    if (desktop) {
      Object.defineProperty(window, 'chrome', {
        configurable: true,
        value: { webview: { postMessage: vi.fn() } },
      })
    }
    const { default: router } = await import('@/router')
    expect(router.getRoutes().map((route) => route.path).sort()).toEqual(
      ['/', '/:catchAll(.*)', '/setup', ...pages.map((page) => `/${page}`)].sort(),
    )
    expect(router.getRoutes().filter((route) => route.name).map((route) => route.name).sort()).toEqual(
      [...pages, 'setup'].sort(),
    )
    await router.push('/setup')
    expect(router.currentRoute.value.name).toBe(desktop ? 'core' : 'setup')

    const settings = await import('@/store/settings')
    const { renderRoutes } = await import('@/helper')
    settings.splitOverviewPage.value = true
    settings.isSidebarCollapsed.value = true
    expect(renderRoutes.value).toEqual(pages)
    const { default: Sidebar } = await import('@/components/sidebar/Sidebar.vue')
    const { i18n } = await import('@/i18n')
    app = createApp(Sidebar)
    app.use(router)
    app.use(i18n)
    root = document.createElement('div')
    document.body.appendChild(root)
    app.mount(root)
    await nextTick()
    const navigation = () => [...root!.querySelectorAll('[data-sidebar-route]')].map(
      (item) => item.getAttribute('data-sidebar-route'),
    )
    expect(navigation()).toEqual(pages)
    const coreLink = root.querySelector<HTMLAnchorElement>('[data-sidebar-route=core] > a')!
    expect(coreLink.getAttribute('href')).toContain('/core')
    expect(coreLink.getAttribute('aria-label')).toBe(i18n.global.t('core'))
    coreLink.focus()
    expect(document.activeElement).toBe(coreLink)
    coreLink.click()
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('core'))
    settings.splitOverviewPage.value = false
    await nextTick()
    expect(navigation()).toEqual(pages.filter((page) => page !== 'overview'))
    // Embedding overview removes only its navigation item; the data page remains a valid route.
    expect(router.hasRoute('overview')).toBe(true)
  })
})
