import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

let app: App | undefined
let root: HTMLDivElement
let scroll: ReturnType<typeof vi.fn>
let props: { scrollTo: string | null }

beforeEach(async () => {
  vi.resetModules()
  localStorage.clear()
  root = document.createElement('div')
  document.body.append(root)
  scroll = vi.fn()
  vi.stubGlobal('requestAnimationFrame', (fn: FrameRequestCallback) => {
    fn(0)
    return 1
  })
  vi.spyOn(HTMLElement.prototype, 'scrollIntoView').mockImplementation(function () {
    scroll(this.id, root.querySelector('[data-height="400"]') !== null)
  })
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function () {
    return Number(this.querySelector('[data-height]')?.getAttribute('data-height') ?? 0)
  })
  vi.doMock('@/composables/hostBridge', () => ({ hasHostBridge: false, hostState: ref({}) }))
  vi.doMock('@vueuse/core', async (original) => ({
    ...(await original<typeof import('@vueuse/core')>()),
    useElementSize: () => ({ width: ref(1200), height: ref(0) }),
  }))
  vi.doMock('@/components/settings/backend/BackendSettings.vue', () => ({
    default: () => h('div'),
  }))
  vi.doMock('@/components/settings/general/AboutDashboardSettings.vue', () => ({
    default: () => h('div'),
  }))
  vi.doMock('@/components/settings/general/ZashboardSettings.vue', () => ({
    __esModule: true,
    default: () => h('div', { 'data-height': 100 }),
  }))
  vi.doMock('@/components/settings/overview/OverviewSettings.vue', () => ({
    __esModule: true,
    default: () => h('div', { 'data-height': 80 }),
  }))
  vi.doMock('@/components/settings/proxies/ProxiesSettings.vue', () => ({
    __esModule: true,
    default: () => h('div', { 'data-height': 70 }),
  }))
  vi.doMock('@/components/settings/connections/ConnectionsSettings.vue', () => ({
    default: () => h('div', { 'data-height': 400 }),
  }))
  const { default: SettingsContent } = await import('@/components/settings/SettingsContent.vue')
  const { reactive } = await import('vue')
  props = reactive({ scrollTo: 'connectionSettings' })
  app = createApp({ render: () => h(SettingsContent, props) })
  app.config.globalProperties.$t = (key: string) => key
  app.mount(root)
})

afterEach(() => {
  app?.unmount()
  root.remove()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('first navigation balances rendered settings columns before scrolling', async () => {
  await vi.waitFor(() =>
    expect(scroll).toHaveBeenCalledWith('settings-content-connectionSettings', true),
  )
  const columns = [...root.querySelectorAll('.grid-cols-2 > div')].map((column) =>
    [...column.querySelectorAll('[data-key]')].map((item) => item.getAttribute('data-key')),
  )
  expect(columns).toEqual([
    ['generalSettings', 'connectionSettings'],
    ['overviewSettings', 'proxySettings'],
  ])
})

it('collapsing settings cancels a pending navigation', async () => {
  scroll.mockClear()
  root.querySelector<HTMLButtonElement>('[data-testid=settings-toggle]')!.click()
  await nextTick()
  await nextTick()
  expect(scroll).not.toHaveBeenCalled()
  expect(root.querySelector('[data-key=connectionSettings]')).toBeNull()
})

it('a backend navigation supersedes the pending settings target', async () => {
  scroll.mockClear()
  props.scrollTo = 'backendSettings'
  await nextTick()
  await nextTick()
  expect(scroll).toHaveBeenCalledWith('settings-content-backendSettings', true)
  expect(scroll.mock.calls.every(([target]) => target === 'settings-content-backendSettings')).toBe(true)
})
