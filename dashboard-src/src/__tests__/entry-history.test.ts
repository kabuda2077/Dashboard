import { afterEach, expect, it, vi } from 'vitest'
import { createWebHashHistory } from 'vue-router'

vi.mock('@/api/http', () => ({}))
vi.mock('@/helper/dayjs', () => ({}))
vi.mock('@/App.vue', () => ({ default: {} }))
vi.mock('@/assets/load-fonts', () => ({ loadFonts: vi.fn() }))
vi.mock('@/composables/hostBridge', () => ({ postHostMessage: vi.fn() }))
vi.mock('@/helper', () => ({ applyCustomThemes: vi.fn() }))
vi.mock('@/helper/dashboardSettingsSync', () => ({ installDashboardSettingsSync: vi.fn() }))
vi.mock('@/hostBootstrap', () => ({}))
vi.mock('@/i18n', () => ({ i18n: {} }))
vi.mock('@/router', () => ({ default: {} }))
vi.mock('vue', async (original) => ({
  ...(await original<typeof import('vue')>()),
  createApp: () => ({
    use() {
      return this
    },
    mount: vi.fn(),
  }),
}))

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  history.replaceState(null, '', '/')
})

it('the production entry preserves native history updates while Edge is hidden', async () => {
  vi.stubGlobal('navigator', { userAgent: 'Mozilla/5.0 Edg/140.0.0.0' })
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
  const nativeReplace = history.replaceState
  await import('@/appEntry')
  expect(history.replaceState).toBe(nativeReplace)
  const routing = createWebHashHistory()
  try {
    routing.replace('/rules')
    expect(routing.location).toBe('/rules')
    expect(location.hash).toBe('#/rules')
    expect(history.state.current).toBe('/rules')
    document.dispatchEvent(new Event('visibilitychange'))
    expect(location.hash).toBe('#/rules')
  } finally {
    routing.destroy()
  }
})
