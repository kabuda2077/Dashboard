import axios, { AxiosError } from 'axios'
import { createApp, h, nextTick, type App } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { afterEach, expect, it, vi } from 'vitest'

const { push, notification } = vi.hoisted(() => ({ push: vi.fn(), notification: vi.fn() }))
vi.mock('@/router', () => ({ default: { push } }))
vi.mock('@/helper/notification', () => ({ showNotification: notification }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: { value: false } }))
vi.mock('@/components/common/CtrlsBar.vue', () => ({ default: { setup: (_: unknown, { slots }: { slots: { default: () => unknown } }) => () => h('div', slots.default()) } }))
vi.mock('@/components/settings/SettingsContent.vue', () => ({ default: { render: () => null } }))
vi.mock('@/router/pageLoaders', () => ({ scheduleAfterInitialPaint: () => vi.fn(), preloadSecondaryPages: vi.fn() }))
let app: App | undefined, root: HTMLElement | undefined

afterEach(() => { app?.unmount(); root?.remove(); Reflect.deleteProperty(window, 'chrome') })

it('desktop 401 edits host credentials and reconnects only with acknowledged host state', async () => {
  let receive!: (event: MessageEvent) => void
  const post = vi.fn()
  Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
    postMessage: post, addEventListener: (_: string, handler: typeof receive) => { receive = handler }, removeEventListener: vi.fn(),
  } } })
  await import('@/hostBootstrap')
  await import('@/api/http')
  const { activeBackend, activeUuid } = await import('@/store/setup')
  const state = { coreType: 'mihomo', isRunning: true, processId: 1, setupCompleted: true,
    apiUrl: 'http://localhost:9090', secret: 'old', mihomoApiUrl: 'http://localhost:9090', mihomoSecret: 'old' }
  const send = async (value: object) => {
    receive(new MessageEvent('message', { data: { type: 'state', state: value } })); await nextTick()
  }
  await send(state)
  const uuid = activeUuid.value
  const unauthorized = () => axios.get('/configs', { adapter: async (config) => {
    throw new AxiosError('Unauthorized', '401', config, null, { config, data: {}, headers: {}, status: 401, statusText: 'Unauthorized' })
  } })
  await expect(unauthorized()).rejects.toBeInstanceOf(AxiosError)
  await expect(unauthorized()).rejects.toBeInstanceOf(AxiosError)
  await nextTick()
  expect(notification).toHaveBeenCalledTimes(1)
  expect(push).toHaveBeenCalledTimes(1)
  expect(activeUuid.value).toBe(uuid)
  expect(push).toHaveBeenCalledWith({ name: 'core', query: { connection: 'unauthorized' } })

  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/core', name: 'core', component: { render: () => null } }] })
  await router.push('/core?connection=unauthorized')
  const { default: Core } = await import('@/views/CorePage.vue')
  app = createApp(Core); app.use(router)
  root = document.createElement('div'); document.body.appendChild(root); app.mount(root)
  await nextTick()
  expect(root.textContent).toContain('连接认证失败')
  const secret = root.querySelectorAll<HTMLInputElement>('input[type=text]')[3]!
  secret.value = 'correct'; secret.dispatchEvent(new Event('input', { bubbles: true }))
  const save = [...root.querySelectorAll('button')].find((button) => button.textContent?.trim() === '保存')!
  save.click(); await nextTick()
  expect(post).toHaveBeenCalledWith(expect.objectContaining({ type: 'save', mihomoSecret: 'correct' }))
  expect(activeBackend.value?.password).toBe('old')
  await send({ ...state, secret: 'correct', mihomoSecret: 'correct' })
  expect(activeUuid.value).toBe(uuid)
  expect(activeBackend.value?.password).toBe('correct')
  await send({ ...state, secret: 'correct', mihomoSecret: 'correct', latestCoreVersion: 'new' })
  expect(activeBackend.value?.password).toBe('correct')
  await axios.get('/configs', { adapter: async (config) => {
    expect(config.headers.Authorization).toBe('Bearer correct')
    return { config, data: {}, headers: {}, status: 200, statusText: 'OK' }
  } })
})
