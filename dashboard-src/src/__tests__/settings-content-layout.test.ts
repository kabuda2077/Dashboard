import SettingsContent from '@/components/settings/SettingsContent.vue'
import { SETTINGS_MENU_KEY } from '@/constant'
import { createApp, nextTick, ref, type App } from 'vue'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const size = { width: ref(0), height: ref(0) }

vi.mock('@vueuse/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@vueuse/core')>()),
  useElementSize: () => size,
}))

// Async Vue loaders need an ES-module-shaped result to unwrap the default
// component in test/development mode, not a module object rendered as a VNode.
vi.mock('@/components/settings/backend/BackendSettings.vue', () => ({ __esModule: true, default: () => null }))
vi.mock('@/components/settings/connections/ConnectionsSettings.vue', () => ({ __esModule: true, default: () => null }))
vi.mock('@/components/settings/general/AboutDashboardSettings.vue', () => ({ __esModule: true, default: () => null }))
vi.mock('@/components/settings/general/ZashboardSettings.vue', () => ({ __esModule: true, default: () => null }))
vi.mock('@/components/settings/overview/OverviewSettings.vue', () => ({ __esModule: true, default: () => null }))
vi.mock('@/components/settings/proxies/ProxiesSettings.vue', () => ({ __esModule: true, default: () => null }))

let app: App | undefined
let host: HTMLElement | undefined

const mountExpandedSettings = async () => {
  host = document.createElement('div')
  document.body.appendChild(host)
  app = createApp(SettingsContent)
  app.config.globalProperties.$t = (key: string) => key
  app.mount(host)
  host.querySelector('button')!.click()
  await vi.waitFor(() => expect(host!.querySelector('[data-key="connectionSettings"]')).not.toBeNull())
  await nextTick()
}

beforeEach(() => {
  localStorage.clear()
  size.width.value = 0
})

afterEach(() => {
  app?.unmount()
  host?.remove()
  app = undefined
  host = undefined
  localStorage.clear()
})

it('uses fixed menu order regardless of legacy ordering data', async () => {
  localStorage.setItem(
    'config/settings-menu-order',
    JSON.stringify([
      SETTINGS_MENU_KEY.connections,
      SETTINGS_MENU_KEY.proxies,
      SETTINGS_MENU_KEY.overview,
      SETTINGS_MENU_KEY.general,
    ]),
  )
  size.width.value = 999
  await mountExpandedSettings()

  expect([...host!.querySelectorAll<HTMLElement>('[data-key]')].map((item) => item.dataset.key)).toEqual([
    SETTINGS_MENU_KEY.backend,
    SETTINGS_MENU_KEY.general,
    SETTINGS_MENU_KEY.overview,
    SETTINGS_MENU_KEY.proxies,
    SETTINGS_MENU_KEY.connections,
  ])
})

it('switches at 1000px and ignores the removed two-column preference', async () => {
  localStorage.setItem('config/settings-page-two-columns', 'false')
  size.width.value = 999
  await mountExpandedSettings()

  expect(host!.querySelector('.grid-cols-2')).toBeNull()

  size.width.value = 1000
  await nextTick()
  await nextTick()

  expect(host!.querySelector('.grid-cols-2')).not.toBeNull()
})
