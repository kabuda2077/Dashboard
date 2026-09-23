import { createApp, h, nextTick, type App } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, expect, it, vi } from 'vitest'

vi.mock('@/components/common/CtrlsBar.vue', () => ({ default: { setup: (_: unknown, { slots }: { slots: { default: () => unknown } }) => () => h('div', slots.default()) } }))
vi.mock('@/components/settings/SettingsContent.vue', () => ({ default: { render: () => null } }))
vi.mock('@/router/pageLoaders', () => ({ scheduleAfterInitialPaint: () => vi.fn(), preloadSecondaryPages: vi.fn() }))
let app: App | undefined, root: HTMLElement | undefined

afterEach(() => { app?.unmount(); root?.remove(); Reflect.deleteProperty(window, 'chrome') })

it('production Core preserves editing across updates and submits both core drafts before switching', async () => {
  vi.resetModules()
  let receive!: (event: MessageEvent) => void
  const post = vi.fn()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: post, addEventListener: (_: string, handler: typeof receive) => { receive = handler }, removeEventListener: vi.fn(),
  } } })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/core', name: 'core', component: { render: () => null } }] })
  await router.push('/core')
  const { default: Core } = await import('@/views/CorePage.vue')
  app = createApp(Core); app.use(router)
  root = document.createElement('div'); document.body.appendChild(root); app.mount(root)
  await nextTick()
  const state = { coreType: 'mihomo', isRunning: true, setupCompleted: true,
    mihomoCorePath: 'mihomo.exe', mihomoConfigPath: 'config.yaml', mihomoApiUrl: 'http://localhost:9090', mihomoSecret: 'old',
    singBoxCorePath: 'sing-box.exe', singBoxConfigPath: 'config.json', singBoxApiUrl: 'http://localhost:9091', singBoxSecret: 'box',
  }
  const send = async (next: object) => { receive(new MessageEvent('message', { data: { type: 'state', state: next } })); await nextTick() }
  await send(state)
  const inputs = () => [...root!.querySelectorAll<HTMLInputElement>('input[type=text]')]
  const secret = inputs()[3]
  secret.value = 'typing'; secret.dispatchEvent(new Event('input', { bubbles: true }))
  await send({ ...state, latestCoreVersion: 'new', mihomoConfigPath: 'new.yaml' })
  expect(inputs()[3].value).toBe('typing')
  expect(inputs()[1].value).toBe('new.yaml')
  const click = async (text: string) => {
    const button = [...root!.querySelectorAll('button')].find((item) => item.textContent?.trim() === text)!
    button.click(); await nextTick()
  }
  await click('切换')
  expect(root.textContent).toContain('先保存当前编辑的配置')
  await click('确定')
  expect(post).toHaveBeenCalledWith(expect.objectContaining({ type: 'switchCore', targetCoreType: 'sing-box', mihomoSecret: 'typing', singBoxSecret: 'box' }))
  const command = post.mock.calls.find(([message]) => message.type === 'switchCore')![0]
  for (const alias of ['corePath', 'configPath', 'apiUrl', 'secret']) expect(command).not.toHaveProperty(alias)
  await send({ ...state, coreType: 'sing-box', mihomoSecret: 'typing', mihomoConfigPath: 'new.yaml' })
  expect(inputs()[3].value).toBe('box')
  await send({ ...state, coreType: 'mihomo', mihomoSecret: 'typing', mihomoConfigPath: 'new.yaml' })
  expect(inputs()[3].value).toBe('typing')
  // Once the host has acknowledged the draft, a later authoritative change is accepted.
  await send({ ...state, mihomoSecret: 'changed elsewhere' })
  expect(inputs()[3].value).toBe('changed elsewhere')
})

it('keeps initial setup visible after rejection, avoids automatic resubmission, and closes only on host confirmation', async () => {
  vi.resetModules()
  let receive!: (event: MessageEvent) => void
  const post = vi.fn()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: post, addEventListener: (_: string, handler: typeof receive) => { receive = handler }, removeEventListener: vi.fn(),
  } } })
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/core', name: 'core', component: { render: () => null } }] })
  await router.push('/core')
  const { default: Core } = await import('@/views/CorePage.vue')
  app = createApp(Core); app.use(router)
  root = document.createElement('div'); document.body.appendChild(root); app.mount(root)
  const state = { coreType: 'mihomo', isRunning: true, setupCompleted: false }
  const send = async (next: object) => { receive(new MessageEvent('message', { data: { type: 'state', state: next } })); await nextTick() }
  const completions = () => post.mock.calls.filter(([message]) => message.type === 'completeSetup')
  await send(state)
  expect(completions()).toHaveLength(1)
  expect(completions()[0][0].setupCompleted).toBe(false)
  expect(root.textContent).toContain('首次启动设置')
  // A rejected operation republishes unchanged host state. It must not form a message loop.
  await send(state)
  await send({ ...state, coreVersion: 'v1' })
  expect(completions()).toHaveLength(1)
  expect(root.textContent).toContain('首次启动设置')
  const retry = [...root.querySelectorAll('button')].find(button => button.textContent?.trim() === '完成设置')!
  retry.click(); await nextTick()
  expect(completions()).toHaveLength(2)
  expect(root.textContent).toContain('首次启动设置')
  await send({ ...state, setupCompleted: true })
  expect(root.textContent).not.toContain('首次启动设置')
})
