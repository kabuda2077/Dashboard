import { createApp, h, nextTick } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { expect, it, vi } from 'vitest'
vi.mock('@/components/common/CtrlsBar.vue', () => ({ default: { setup: (_: unknown, { slots }: { slots: { default: () => unknown } }) => () => h('div', slots.default()) } }))
vi.mock('@/components/settings/SettingsContent.vue', () => ({ default: { render: () => null } }))
vi.mock('@/router/pageLoaders', () => ({ scheduleAfterInitialPaint: () => vi.fn(), preloadSecondaryPages: vi.fn() }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: { value: false } }))

it('Core requires explicit replacement confirmation for an unreadable credential, including empty', async () => {
  const post = vi.fn()
  let receive!: (event: MessageEvent) => void
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: post, addEventListener: (_: string, listener: typeof receive) => { receive = listener }, removeEventListener: vi.fn(),
  } } })
  const bridge = await import('@/composables/hostBridge')
  const state = { coreType: 'mihomo', apiUrl: 'http://localhost:9090', setupCompleted: true,
    secret: '', secretDecryptionFailed: true, mihomoSecretDecryptionFailed: true }
  bridge.applyHostState(state)
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/core', name: 'core', component: { render: () => null } }] })
  await router.push('/core')
  const { default: Core } = await import('@/views/CorePage.vue')
  const app = createApp(Core); app.use(router)
  const root = document.createElement('div'); document.body.appendChild(root); app.mount(root)
  try {
    await nextTick()
    expect(root.textContent).toContain('Secret 无法解密')
    const save = [...root.querySelectorAll('button')].find((button) => button.textContent?.trim() === '保存')!
    save.click()
    expect(post).toHaveBeenLastCalledWith(expect.objectContaining({ type: 'save', replaceMihomoSecret: false }))
    const confirmation = root.querySelector<HTMLInputElement>('[role=alert] input[type=checkbox]')!
    confirmation.checked = true; confirmation.dispatchEvent(new Event('change', { bubbles: true }))
    await nextTick(); save.click()
    expect(post).toHaveBeenLastCalledWith(expect.objectContaining({ replaceMihomoSecret: true, mihomoSecret: '', replaceSingBoxSecret: false }))
    receive(new MessageEvent('message', { data: { type: 'state', state: { ...state, secretDecryptionFailed: false, mihomoSecretDecryptionFailed: false } } }))
    await nextTick()
    expect(root.textContent).not.toContain('Secret 无法解密')
  } finally { app.unmount(); root.remove(); Reflect.deleteProperty(window, 'chrome') }
})
