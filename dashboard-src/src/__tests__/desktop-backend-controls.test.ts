import { createApp, nextTick } from 'vue'
import { afterEach, expect, it, vi } from 'vitest'
vi.mock('@/components/sidebar/SidebarStatistics.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/sidebar/SidebarButtons.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/backend/BackendSwitch.vue', () => ({ default: { template: '<select data-backend-picker />' } }))
afterEach(() => { Reflect.deleteProperty(window, 'chrome') })

it.each([true, false])('shows backend selection only outside the desktop host (desktop=%s)', async (desktop) => {
  vi.resetModules()
  if (desktop) Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: { postMessage: vi.fn() } } })
  const { default: Controls } = await import('@/components/sidebar/CommonCtrl.vue')
  const root = document.createElement('div')
  const app = createApp(Controls)
  app.mount(root)
  try {
    await nextTick()
    expect(!!root.querySelector('[data-backend-picker]')).toBe(!desktop)
  } finally { app.unmount() }
})
