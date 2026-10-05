import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

let app: App | undefined
let root: HTMLDivElement
let release: () => void
let scroll: ReturnType<typeof vi.fn>
let props: { scrollTo: string | null }

beforeEach(async () => {
  vi.resetModules()
  release = undefined as unknown as () => void
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
  vi.doMock('@/components/settings/connections/ConnectionsSettings.vue', async () => {
    await new Promise<void>((resolve) => {
      release = resolve
    })
    return { __esModule: true, default: () => h('div', { 'data-height': 400 }) }
  })
  const { default: SettingsContent } = await import('@/components/settings/SettingsContent.vue')
  const { reactive } = await import('vue')
  props = reactive({ scrollTo: 'connectionSettings' })
  app = createApp({ render: () => h(SettingsContent, props) })
  app.config.globalProperties.$t = (key: string) => key
  app.mount(root)
  await vi.waitFor(() => expect(release).toBeTypeOf('function'))
})

afterEach(() => {
  app?.unmount()
  release?.()
  root.remove()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

it('first navigation waits for lazy settings before balancing columns and scrolling', async () => {
  expect(scroll).not.toHaveBeenCalled()
  release()
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

it('collapsing settings cancels a pending lazy navigation', async () => {
  root.querySelector<HTMLButtonElement>('[data-testid=settings-toggle]')!.click()
  await nextTick()
  release()
  await new Promise((resolve) => setTimeout(resolve, 20))
  expect(scroll).not.toHaveBeenCalled()
  expect(root.querySelector('[data-key=connectionSettings]')).toBeNull()
})

it('a backend navigation supersedes the delayed settings target', async () => {
  props.scrollTo = 'backendSettings'
  await nextTick()
  await vi.waitFor(() =>
    expect(scroll).toHaveBeenCalledWith('settings-content-backendSettings', false),
  )
  scroll.mockClear()
  release()
  await nextTick()
  await new Promise((resolve) => setTimeout(resolve, 20))
  expect(scroll).not.toHaveBeenCalled()
})
