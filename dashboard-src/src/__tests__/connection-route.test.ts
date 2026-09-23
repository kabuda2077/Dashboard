import { afterEach, expect, it, vi } from 'vitest'
import { ref } from 'vue'

vi.mock('@/views/CorePage.vue', () => ({ default: { render: () => null } }))
vi.mock('@/views/HomePage.vue', () => ({ default: { render: () => null } }))
vi.mock('@/views/SetupPage.vue', () => ({ default: { render: () => null } }))
vi.mock('@/router/pageLoaders', () => ({ loadConnectionsPage: vi.fn(), loadOverviewPage: vi.fn(), loadProxiesPage: vi.fn() }))
vi.mock('@/assembly/backend', () => ({ capabilities: ref({ rules: true }) }))
vi.mock('@/helper', () => ({ renderRoutes: ref(['core', 'proxies']) }))
vi.mock('@/store/settings', () => ({ language: ref('en') }))
vi.mock('@/store/setup', () => ({ activeBackend: ref(null) }))
vi.mock('@/i18n', () => ({ i18n: { global: { t: (key: string) => key } } }))
afterEach(() => { Reflect.deleteProperty(window, 'chrome') })

it('desktop setup and missing endpoint route to Core', async () => {
  vi.resetModules()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: { postMessage: vi.fn() } } })
  const { default: router } = await import('@/router')
  await router.push('/setup?editBackend=old')
  expect(router.currentRoute.value.name).toBe('core')
  await router.push('/proxies')
  expect(router.currentRoute.value.name).toBe('core')
})

it('browser setup remains accessible when no backend exists', async () => {
  vi.resetModules()
  const { default: router } = await import('@/router')
  await router.push('/setup?editBackend=old')
  expect(router.currentRoute.value.name).toBe('setup')
  expect(router.currentRoute.value.query.editBackend).toBe('old')
})
