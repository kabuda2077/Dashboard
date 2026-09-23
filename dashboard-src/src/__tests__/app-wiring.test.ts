import { SETTINGS_MENU_KEY } from '@/constant'
import { createApp, nextTick, ref, type App } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// Mount the production App, not a test component that installs useKeyboard.
// Stub unrelated visuals/transport; keep App wiring and the keyboard hook real.
describe('production App desktop wiring', () => {
  let apps: App[]
  let roots: HTMLElement[]
  let router: Router
  let enabled: ReturnType<typeof ref<boolean>>
  let importSettings: ReturnType<typeof vi.fn>
  let showNotification: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.resetModules()
    localStorage.clear()
    sessionStorage.clear()
    apps = []
    roots = []
    enabled = ref(false)
    importSettings = vi.fn().mockResolvedValue(false)
    showNotification = vi.fn()
    Object.defineProperty(window, 'chrome', {
      configurable: true,
      value: { webview: { postMessage: vi.fn() } },
    })
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/core', name: 'core', component: { render: () => null } },
        { path: '/proxies', name: 'proxies', component: { render: () => null } },
      ],
    })
    vi.doMock('@/router', () => ({ default: router }))
    vi.doMock('@/helper', () => ({ renderRoutes: ref(['core', 'proxies']) }))
    vi.doMock('@/helper/indexeddb', () => ({ backgroundImage: ref(null) }))
    vi.doMock('@/composables/useAppearanceVars', () => ({ useAppearanceVars: vi.fn() }))
    vi.doMock('@/components/common/ConfirmDialogHost.vue', () => ({
      default: { render: () => null },
    }))
    vi.doMock('@/helper/notification', () => ({ initNotification: vi.fn(), showNotification }))
    vi.doMock('@/helper/autoImportSettings', () => ({
      autoImportSettings: enabled,
      importSettingsFromUrl: importSettings,
    }))
  })

  afterEach(() => {
    apps.forEach((app) => app.unmount())
    roots.forEach((root) => root.remove())
    Reflect.deleteProperty(window, 'chrome')
  })

  const mountApp = async () => {
    await router.push('/proxies')
    const { default: AppComponent } = await import('@/App.vue')
    const app = createApp(AppComponent)
    app.use(router)
    const root = document.createElement('div')
    document.body.appendChild(root)
    apps.push(app)
    roots.push(root)
    app.mount(root)
    await nextTick()
    return { app, root }
  }

  const press = (key: string, target: EventTarget = document) => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
    target.dispatchEvent(event)
    return event
  }

  it('installs shortcuts through App and removes them on unmount', async () => {
    const { app } = await mountApp()
    const settings = await import('@/store/settings')
    settings.manageHiddenGroup.value = false
    expect(press('h').defaultPrevented).toBe(true)
    expect(settings.manageHiddenGroup.value).toBe(true)
    const before = settings.isSidebarCollapsed.value
    press('b')
    expect(settings.isSidebarCollapsed.value).toBe(!before)
    app.unmount()
    apps = apps.filter((item) => item !== app)
    press('h')
    expect(settings.manageHiddenGroup.value).toBe(true)
  })

  it('does not intercept editing controls', async () => {
    const { root } = await mountApp()
    const settings = await import('@/store/settings')
    settings.manageHiddenGroup.value = false
    for (const tag of ['input', 'textarea', 'select']) {
      const input = document.createElement(tag)
      root.appendChild(input)
      expect(press('h', input).defaultPrevented).toBe(false)
    }
    expect(settings.manageHiddenGroup.value).toBe(false)
  })

  it('keeps desktop backend switching disabled and opens Core settings with S', async () => {
    await mountApp()
    const setup = await import('@/store/setup')
    const backend = {
      type: 'clash' as const, protocol: 'http', host: '127.0.0.1', port: '9090',
      secondaryPath: '', password: '',
    }
    setup.backendList.value = [{ ...backend, uuid: 'a' }, { ...backend, uuid: 'b' }]
    setup.activeUuid.value = 'a'
    expect(press('p').defaultPrevented).toBe(false)
    expect(press('n').defaultPrevented).toBe(false)
    expect(setup.activeUuid.value).toBe('a')
    press('s')
    await vi.waitFor(() => expect(router.currentRoute.value.name).toBe('core'))
    expect(router.currentRoute.value.query.scrollTo).toBe(SETTINGS_MENU_KEY.backend)
    expect(setup.showBackendSettingsDialog.value).toBe(false)
  })

  it('does not import settings when the startup toggle is disabled', async () => {
    await mountApp()
    expect(importSettings).not.toHaveBeenCalled()
  })

  it('imports once per document, including concurrent App mounts', async () => {
    enabled.value = true
    let complete!: (value: boolean) => void
    importSettings.mockReturnValue(new Promise<boolean>((resolve) => { complete = resolve }))
    await mountApp()
    await mountApp()
    expect(importSettings).toHaveBeenCalledTimes(1)
    expect(importSettings).toHaveBeenCalledWith()
    complete(false) // Existing helper owns confirmation; declining must not force import.
    await nextTick()
  })

  it('reports startup import failures instead of leaving a rejected mount task', async () => {
    enabled.value = true
    importSettings.mockRejectedValue(new Error('import unavailable'))
    await mountApp()
    await vi.waitFor(() => expect(showNotification).toHaveBeenCalledWith(
      expect.objectContaining({ content: 'import unavailable', type: 'alert-error' }),
    ))
    expect(importSettings).toHaveBeenCalledTimes(1)
  })
})
