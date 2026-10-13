import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'
import { startMockHost } from './hostFixture'
const { get, patch } = vi.hoisted(() => ({ get: vi.fn(), patch: vi.fn() }))
vi.mock('@/api/clash', () => ({
  getConfigsAPI: get,
  patchConfigsAPI: patch,
  flushDNSCacheAPI: vi.fn(),
  flushFakeIPAPI: vi.fn(),
  reloadConfigsAPI: vi.fn(),
  updateGeoDataAPI: vi.fn(),
}))
vi.mock('@/assembly/proxies', () => ({
  fetchProxies: vi.fn(),
  flushSmartGroupWeightsAPI: vi.fn(),
  hasSmartGroup: false,
}))
vi.mock('@/assembly/rules', () => ({ fetchRules: vi.fn() }))
vi.mock('@/assembly/version', () => ({ isSingBoxCore: false }))
vi.mock('@/components/common/BackendVersion.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/backend/TopDownloadConnections.vue', () => ({
  default: { render: () => null },
}))
let app: App | undefined
let root: HTMLDivElement
beforeEach(() => {
  vi.resetModules()
  localStorage.clear()
  get.mockReset()
  patch.mockReset()
})
afterEach(() => {
  app?.unmount()
  root?.remove()
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
const mount = async (hostPatch = {}) => {
  const host = await startMockHost(hostPatch)
  const state = await import('@/assembly/config')
  const { useBackendRuntimeConfig } = await import('@/composables/useBackendRuntimeConfig')
  const view = useBackendRuntimeConfig()
  const { i18n } = await import('@/i18n')
  i18n.global.locale.value = 'en-US'
  const { default: Component } = await import('@/components/settings/backend/BackendSettings.vue')
  root = document.createElement('div')
  root.id = 'app-content'
  document.body.append(root)
  app = createApp(Component)
  app.use(i18n)
  app.mount(root)
  const load = async (data: unknown) => {
    get.mockResolvedValueOnce({ data })
    await state.fetchConfigs(true)
    await nextTick()
  }
  return { state, view, load, host }
}
const toggle = (label: string) =>
  root.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`)

it('distinguishes not-loaded, loading, malformed/unknown and confirmed false without writable unknown toggles', async () => {
  const { state, view, load } = await mount()
  expect(root.textContent).toContain('Not loaded')
  expect(root.querySelector('input[type=checkbox]')).toBeNull()
  let resolve!: (value: unknown) => void
  get.mockImplementationOnce(
    () =>
      new Promise((done) => {
        resolve = done
      }),
  )
  const request = state.fetchConfigs()
  await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(1))
  await nextTick()
  expect(root.querySelectorAll('.loading-spinner')).toHaveLength(2)
  resolve({ data: { tun: {}, 'allow-lan': 'false' } })
  await request
  await nextTick()
  expect(root.textContent).toContain('Unknown state')
  expect(root.querySelector('input[type=checkbox]')).toBeNull()
  await view.updateTunEnabled(true)
  await view.updateAllowLan(true)
  expect(patch).not.toHaveBeenCalled()
  await load({ tun: { enable: false }, 'allow-lan': false })
  expect(toggle('TUN mode')?.checked).toBe(false)
  expect(toggle('TUN mode')?.disabled).toBe(false)
  expect(toggle('Allow LAN')?.checked).toBe(false)
})

it('shows loading while the host is checking readiness, before any configuration GET', async () => {
  const { view } = await mount({ apiStatus: 'checking' })
  expect(view.configStatus.value).toBe('loading')
  expect(root.querySelectorAll('.loading-spinner')).toHaveLength(2)
  expect(root.querySelector('input[type=checkbox]')).toBeNull()
  expect(root.querySelector('[role=alert]')).toBeNull()
  expect(get).not.toHaveBeenCalled()
})

it('shows a retryable failure instead of stale switches or an endless spinner, then recovers', async () => {
  const { state, view, load } = await mount()
  await load({ tun: { enable: true }, 'allow-lan': true })
  const failure = Object.assign(new Error('Unauthorized <img src=x>'), {
    isAxiosError: true,
    response: { status: 401 },
  })
  get.mockRejectedValueOnce(failure)
  await expect(state.fetchConfigs(true)).rejects.toBe(failure)
  await nextTick()
  expect(view.configStatus.value).toBe('error')
  expect(root.querySelector('.loading-spinner')).toBeNull()
  expect(root.querySelector('input[type=checkbox]')).toBeNull()
  expect(root.querySelector('[role=alert]')?.textContent).toContain('Unauthorized <img src=x>')
  expect(root.querySelector('img')).toBeNull()
  await view.updateTunEnabled(false)
  await view.updateAllowLan(false)
  expect(patch).not.toHaveBeenCalled()
  get.mockResolvedValueOnce({ data: { tun: { enable: false }, 'allow-lan': false } })
  root.querySelector<HTMLButtonElement>('[role=alert] button')!.click()
  await vi.waitFor(() => expect(toggle('Allow LAN')).not.toBeNull())
  expect(get).toHaveBeenCalledTimes(3)
  expect(root.querySelector('[role=alert]')).toBeNull()
})

it.each([false, true])(
  'shows host-derived read-only TUN (%s) even while the API cannot be read',
  async (enabled) => {
    const { state, view } = await mount({ coreType: 'sing-box', readOnlyTunEnabled: enabled })
    get.mockRejectedValueOnce(new Error('API unavailable'))
    await state.fetchConfigs().catch(() => {})
    await nextTick()
    expect(toggle('TUN mode')?.checked).toBe(enabled)
    expect(toggle('TUN mode')?.disabled).toBe(true)
    expect(root.textContent).not.toContain('Read-only · core configuration file')
    expect(toggle('TUN mode')?.parentElement?.title).toBe('Read-only · core configuration file')
    expect(toggle('TUN mode')?.getAttribute('aria-description')).toBe('Read-only · core configuration file')
    expect(toggle('Allow LAN')).toBeNull()
    expect(root.querySelector('.loading-spinner')).toBeNull()
    await view.updateTunEnabled(!enabled)
    expect(patch).not.toHaveBeenCalled()
    get.mockResolvedValueOnce({ data: { 'allow-lan': true } })
    await state.fetchConfigs(true)
    await nextTick()
    expect(view.configStatus.value).toBe('ready')
    expect(toggle('TUN mode')?.checked).toBe(enabled)
    expect(toggle('TUN mode')?.disabled).toBe(true)
    expect(toggle('Allow LAN')?.checked).toBe(true)
    expect(toggle('Allow LAN')?.disabled).toBe(false)
  },
)

it.each(['unauthorized', 'unreachable', 'transport-401'] as const)(
  'keeps %s visible after session data is reset and retries through the host',
  async (failure) => {
    const { view, host } = await mount({
      apiStatus: failure === 'transport-401' ? 'ready' : failure,
    })
    if (failure === 'transport-401') {
      const { markBackendUnauthorized } = await import('@/helper/backendSession')
      markBackendUnauthorized()
    }
    await nextTick()
    expect(view.configStatus.value).toBe('error')
    expect(root.querySelector('[role=alert]')?.textContent).toContain(
      failure === 'unreachable' ? 'Unreachable' : 'Authentication failed',
    )
    expect(root.querySelector('input[type=checkbox]')).toBeNull()
    expect(root.querySelector('.loading-spinner')).toBeNull()
    root.querySelector<HTMLButtonElement>('[role=alert] button')!.click()
    const request = host.post.mock.calls.find(
      ([value]) => value.type === 'refreshCoreMetadata',
    )?.[0]
    expect(request).toBeDefined()
    expect(get).not.toHaveBeenCalled()
    host.ack(request.requestId)
  },
)

const stackSelect = () => root.querySelector<HTMLButtonElement>('[role=combobox][aria-label="TUN stack"]')
const chooseStack = async (label: string) => {
  stackSelect()!.click()
  await nextTick()
  const option = [...root.querySelectorAll<HTMLElement>('[role=option]')].find((item) => item.textContent?.trim() === label)
  expect(option).toBeDefined()
  option!.click()
  await nextTick()
}

it('shows normalized confirmed stacks and all three supported choices', async () => {
  const { load } = await mount()
  expect(stackSelect()).toBeNull()
  await load({ tun: { enable: true, stack: 'gVisor' } })
  expect(stackSelect()?.textContent?.trim()).toBe('gvisor')
  stackSelect()!.click()
  await nextTick()
  expect([...root.querySelectorAll('[role=option]')].map((item) => item.textContent?.trim())).toEqual(['system', 'gvisor', 'mixed'])
  expect(root.querySelector('[role=option][aria-selected=true]')?.textContent?.trim()).toBe('gvisor')
})

it('keeps an unknown stack selected and does not write when simply opening the menu', async () => {
  const { load } = await mount()
  await load({ tun: { enable: false, stack: 'FutureStack' } })
  expect(stackSelect()?.textContent?.trim()).toBe('FutureStack')
  stackSelect()!.click()
  await nextTick()
  expect(root.querySelector('[role=option][aria-selected=true]')?.textContent?.trim()).toBe('FutureStack')
  expect(root.querySelector('[role=option][aria-selected=true]')?.getAttribute('aria-disabled')).toBe('true')
  expect(patch).not.toHaveBeenCalled()
})

it('keeps the stack and TUN switch disabled until GET confirms the change', async () => {
  const { load } = await mount()
  await load({ tun: { enable: true, stack: 'System' } })
  let finishPatch!: () => void
  let finishGet!: (value: unknown) => void
  patch.mockImplementationOnce(() => new Promise<void>((resolve) => { finishPatch = resolve }))
  get.mockImplementationOnce(() => new Promise((resolve) => { finishGet = resolve }))
  await chooseStack('mixed')
  expect(patch).toHaveBeenCalledWith({ tun: { enable: true, stack: 'mixed' } })
  expect(stackSelect()?.disabled).toBe(true)
  expect(stackSelect()?.getAttribute('aria-busy')).toBe('true')
  expect(root.textContent).not.toContain('Read-only')
  expect(stackSelect()?.textContent?.trim()).toBe('system')
  expect(toggle('TUN mode')?.disabled).toBe(true)
  finishPatch()
  await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(2))
  expect(stackSelect()?.disabled).toBe(true)
  finishGet({ data: { tun: { enable: true, stack: 'Mixed' } } })
  await vi.waitFor(() => expect(stackSelect()?.disabled).toBe(false))
  expect(stackSelect()?.textContent?.trim()).toBe('mixed')
  expect(toggle('TUN mode')?.checked).toBe(true)
})

it('shows the confirmed stack after rejection and warns when PATCH succeeds without a confirmed rebuild', async () => {
  const notification = await import('@/helper/notification')
  const notify = vi.spyOn(notification, 'showNotification').mockImplementation(() => {})
  try {
    const { load } = await mount()
    await load({ tun: { enable: true, stack: 'System' } })
    patch.mockRejectedValueOnce(new Error('Write rejected'))
    await chooseStack('mixed')
    await vi.waitFor(() => expect(stackSelect()?.disabled).toBe(false))
    expect(stackSelect()?.textContent?.trim()).toBe('system')
    expect(notify.mock.calls.at(-1)?.[0]).toMatchObject({ content: 'Write rejected', type: 'alert-error' })
    patch.mockResolvedValueOnce({})
    get.mockResolvedValueOnce({ data: { tun: { enable: false, stack: 'Mixed' } } })
    await chooseStack('mixed')
    await vi.waitFor(() => expect(notify).toHaveBeenCalledTimes(2))
    expect(toggle('TUN mode')?.checked).toBe(false)
    expect(stackSelect()?.textContent?.trim()).toBe('mixed')
    expect(notify.mock.calls.at(-1)?.[0]).toMatchObject({ content: 'The TUN stack change was not confirmed. Check the current state and core logs.', type: 'alert-error' })
  } finally {
    notify.mockRestore()
  }
})

it('keeps missing or failed API stack capability unavailable in the real settings consumer', async () => {
  const { load, state } = await mount()
  await load({ tun: { enable: true } })
  expect(stackSelect()).toBeNull()
  await load({ tun: { enable: true, stack: 'System' } })
  expect(stackSelect()).not.toBeNull()
  get.mockRejectedValueOnce(new Error('Read failed'))
  await state.fetchConfigs(true).catch(() => {})
  await nextTick()
  expect(stackSelect()).toBeNull()
  expect(root.querySelector('[role=alert]')?.textContent).toContain('Read failed')
})

it.each([
  ['en-US', 'Changing the stack rebuilds TUN and may interrupt connections.'],
  ['zh-CN', '切换协议栈会重建 TUN，可能中断连接。'],
  ['zh-TW', '切換協定堆疊會重建 TUN，可能中斷連線。'],
  ['ru-RU', 'Смена стека пересоздаёт TUN и может прервать соединения.'],
] as const)('exposes the interruption help on focus without a summary row in %s', async (locale, help) => {
  const { load } = await mount()
  const { i18n } = await import('@/i18n')
  i18n.global.locale.value = locale
  await load({ tun: { enable: false, stack: 'System' } })
  const select = root.querySelector<HTMLButtonElement>('[role=combobox]')!
  expect(select.getAttribute('aria-description')).toBe(help)
  expect(select.closest('.setting-item')?.textContent).not.toContain(help)
  select.focus()
  await vi.waitFor(() => expect(root.querySelector('[role=tooltip]')?.textContent).toBe(help))
  select.blur()
})

it('hides the stack for missing capability, failed configuration and desktop sing-box', async () => {
  const { load, state } = await mount({ coreType: 'sing-box', readOnlyTunEnabled: true })
  await load({ tun: { enable: true, stack: 'System' } })
  expect(stackSelect()).toBeNull()
  expect(toggle('TUN mode')?.disabled).toBe(true)
  get.mockRejectedValueOnce(new Error('Read failed'))
  await state.fetchConfigs(true).catch(() => {})
  await nextTick()
  expect(stackSelect()).toBeNull()
})

it('suppresses an obsolete stack failure after a same-endpoint restart', async () => {
  const notification = await import('@/helper/notification')
  const notify = vi.spyOn(notification, 'showNotification').mockImplementation(() => {})
  try {
    const { load, host, state } = await mount()
    await load({ tun: { enable: true, stack: 'System' } })
    let rejectOld!: (error: Error) => void
    patch.mockImplementationOnce(() => new Promise((_, reject) => { rejectOld = reject }))
    await chooseStack('mixed')
    host.runtime({ processId: 2, runtimeEpoch: 2 })
    state.resetConfigs()
    await load({ tun: { enable: false, stack: 'gVisor' } })
    rejectOld(new Error('Old write failed'))
    await vi.waitFor(() => expect(stackSelect()?.disabled).toBe(false))
    expect(stackSelect()?.textContent?.trim()).toBe('gvisor')
    expect(toggle('TUN mode')?.checked).toBe(false)
    expect(notify).not.toHaveBeenCalled()
  } finally {
    notify.mockRestore()
  }
})

it.each(['TUN mode', 'Allow LAN'])(
  'restores the confirmed %s value when a PATCH fails',
  async (label) => {
    const { load } = await mount()
    await load({ tun: { enable: false }, 'allow-lan': false })
    patch.mockRejectedValueOnce(new Error('Write rejected'))
    const input = toggle(label)!
    input.click()
    await vi.waitFor(() => expect(patch).toHaveBeenCalledOnce())
    await vi.waitFor(() => expect(input.disabled).toBe(false))
    expect(input.checked).toBe(false)
  },
)
