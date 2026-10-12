import { createApp, nextTick } from 'vue'
import { createI18n } from 'vue-i18n'
import en from '@/i18n/en'
import { afterEach, expect, it, vi } from 'vitest'
vi.mock('@/components/sidebar/SidebarStatistics.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/sidebar/SidebarButtons.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/backend/BackendSwitch.vue', () => ({ default: { template: '<select data-backend-picker />' } }))
afterEach(() => { Reflect.deleteProperty(window, 'chrome'); vi.restoreAllMocks() })

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

it.each([true, false])('keeps window actions and keyboard settings available in desktop layouts (narrow=%s)', async (narrow) => {
  vi.resetModules()
  vi.spyOn(window, 'matchMedia').mockImplementation((query) => ({
    matches: query === '(max-width: 768px)' && narrow,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }))
  const postMessage = vi.fn()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: { postMessage } } })
  const { default: Toolbar } = await import('@/components/common/CtrlsBar.vue')
  const { default: Shortcuts } = await import('@/components/settings/general/KeyboardShortcutsSettings.vue')
  const { h } = await import('vue')
  const root = document.createElement('div')
  root.id = 'app-content'
  document.body.appendChild(root)
  const app = createApp({ render: () => h('div', [h(Toolbar), h(Shortcuts)]) })
  app.use(createI18n({ legacy: false, locale: 'en', messages: { en } }))
  try {
    app.mount(root)
    await nextTick()
    for (const label of ['最小化', '最大化', '关闭']) {
      const button = root.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)
      expect(button, label).not.toBeNull()
      button!.click()
    }
    expect(postMessage.mock.calls.map(([message]) => message.type)).toEqual(
      expect.arrayContaining(['windowMinimize', 'windowToggleMaximize', 'windowClose']),
    )
    expect(root.textContent).toContain(en.keyboardShortcuts)
    const edit = [...root.querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.parentElement?.textContent?.includes(en.keyboardShortcuts),
    )!
    edit.click()
    await nextTick()
    expect(root.querySelector('input[readonly]')).not.toBeNull()
  } finally {
    app.unmount()
    root.remove()
  }
})
