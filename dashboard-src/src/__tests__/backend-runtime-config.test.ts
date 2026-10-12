import type { Config } from '@/types'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { startMockHost } from './hostFixture'
const { get, patch } = vi.hoisted(() => ({ get: vi.fn(), patch: vi.fn() }))
vi.mock('@/api/clash', () => ({ getConfigsAPI: get, patchConfigsAPI: patch }))
beforeEach(() => {
  vi.resetModules()
  get.mockReset()
  patch.mockReset()
  localStorage.clear()
})
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  vi.useRealTimers()
  Reflect.deleteProperty(window, 'chrome')
})
const config = (value: boolean) => ({ 'allow-lan': value, tun: { enable: value } }) as Config
it('the session owner ignores old config responses after a same-endpoint core restart', async () => {
  const host = await startMockHost()
  let old!: (value: unknown) => void
  get
    .mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          old = resolve
        }),
    )
    .mockResolvedValueOnce({ data: config(true) })
  const state = await import('@/assembly/config')
  state.startConfigRuntime()
  await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(1))
  host.runtime({ processId: 2, runtimeEpoch: 2 })
  state.resetConfigs()
  state.startConfigRuntime()
  await vi.waitFor(() => expect(state.configs.value['allow-lan']).toBe(true))
  old({ data: config(false) })
  await Promise.resolve()
  await Promise.resolve()
  expect(state.configs.value['allow-lan']).toBe(true)
  state.stopConfigRuntime()
})
it('a post-PATCH refresh never reuses a GET started before the write', async () => {
  await startMockHost()
  let old!: (value: unknown) => void
  get
    .mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          old = resolve
        }),
    )
    .mockResolvedValueOnce({ data: config(true) })
  patch.mockResolvedValue({})
  const state = await import('@/assembly/config')
  const first = state.fetchConfigs()
  await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(1))
  await state.updateConfigs({ 'allow-lan': true })
  expect(get).toHaveBeenCalledTimes(2)
  old({ data: config(false) })
  await first
  expect(state.configs.value['allow-lan']).toBe(true)
})
it('retries transient startup failures and derives read-only TUN without component-owned timers', async () => {
  vi.useFakeTimers()
  await startMockHost({ coreType: 'sing-box', readOnlyTunEnabled: true })
  get
    .mockRejectedValueOnce(new Error('not ready'))
    .mockResolvedValueOnce({ data: { 'allow-lan': false } })
  const state = await import('@/assembly/config')
  const { useBackendRuntimeConfig } = await import('@/composables/useBackendRuntimeConfig')
  const view = useBackendRuntimeConfig()
  state.startConfigRuntime()
  await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(1))
  expect(view.tunState.value).toMatchObject({
    visible: true,
    writable: false,
    enabled: true,
    source: 'host',
  })
  await vi.advanceTimersByTimeAsync(801)
  expect(get).toHaveBeenCalledTimes(2)
  expect(view.configStatus.value).toBe('ready')
  state.stopConfigRuntime()
})
const stackConfig = (enable: boolean, stack: unknown) => ({ tun: { enable, stack } })
const loadTun = async (enable = true, stack: unknown = 'System', hostPatch = {}) => {
  const host = await startMockHost(hostPatch)
  const state = await import('@/assembly/config')
  const { useBackendRuntimeConfig } = await import('@/composables/useBackendRuntimeConfig')
  const view = useBackendRuntimeConfig()
  get.mockResolvedValueOnce({ data: stackConfig(enable, stack) })
  await state.fetchConfigs(true)
  return { host, state, view, useBackendRuntimeConfig }
}

it.each([true, false])('preserves confirmed TUN enable=%s and verifies the post-write GET case-insensitively', async (enable) => {
  const { view, state } = await loadTun(enable)
  expect(view.tunStack.value).toBe('system')
  expect(view.tunStackOptions.value.map((option) => option.value)).toEqual(['system', 'gvisor', 'mixed'])
  patch.mockResolvedValue({})
  get.mockResolvedValueOnce({ data: stackConfig(enable, 'gVisor') })
  expect(await view.updateTunStack('gvisor')).toBe(true)
  expect(patch).toHaveBeenCalledWith({ tun: { enable, stack: 'gvisor' } })
  expect(get).toHaveBeenCalledTimes(2)
  expect(state.configs.value.tun.enable).toBe(enable)
  expect(view.tunStack.value).toBe('gvisor')
})

it.each([undefined, '', '  ', null, false, 7])('does not infer stack capability from an absent or malformed stack (%s)', async (stack) => {
  const { view, state } = await loadTun(true, stack === undefined ? null : stack)
  if (stack === undefined) delete state.configs.value.tun.stack
  expect(view.canShowTunStack.value).toBe(false)
  await view.updateTunStack('mixed')
  expect(patch).not.toHaveBeenCalled()
})

it('keeps an unknown current stack without selecting or sending an unsupported value', async () => {
  const { view } = await loadTun(true, 'FutureStack')
  expect(view.tunStack.value).toBe('FutureStack')
  expect(view.tunStackOptions.value[0]).toMatchObject({ value: 'FutureStack', disabled: true })
  await view.updateTunStack('FutureStack')
  await view.updateTunStack('mips')
  expect(patch).not.toHaveBeenCalled()
})

it.each([null, true, false])('does not grant desktop sing-box TUN writes from same-named API fields (host=%s)', async (readOnlyTunEnabled) => {
  const { view } = await loadTun(true, 'System', { coreType: 'sing-box', readOnlyTunEnabled })
  expect(view.canShowTunStack.value).toBe(false)
  expect(view.tunState.value.writable).toBe(false)
  await view.updateTunStack('mixed')
  await view.updateTunEnabled(false)
  expect(patch).not.toHaveBeenCalled()
})

it('serializes TUN writes across consumers through the post-write GET without optimistic changes', async () => {
  const { view, state, useBackendRuntimeConfig } = await loadTun()
  const other = useBackendRuntimeConfig()
  let finishPatch!: () => void
  let finishGet!: (value: unknown) => void
  patch.mockImplementationOnce(() => new Promise<void>((resolve) => { finishPatch = resolve }))
  get.mockImplementationOnce(() => new Promise((resolve) => { finishGet = resolve }))
  const write = view.updateTunStack('mixed')
  expect(other.isTunUpdating.value).toBe(true)
  expect(other.tunState.value.writable).toBe(false)
  expect(view.tunStack.value).toBe('system')
  expect(state.configs.value.tun).toEqual({ enable: true, stack: 'System' })
  await other.updateTunEnabled(false)
  await other.updateTunStack('gvisor')
  expect(patch).toHaveBeenCalledOnce()
  finishPatch()
  await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(2))
  expect(view.isTunUpdating.value).toBe(true)
  expect(view.canShowTunStack.value).toBe(true)
  finishGet({ data: stackConfig(true, 'Mixed') })
  expect(await write).toBe(true)
  expect(view.isTunUpdating.value).toBe(false)
})

it('reports an unconfirmed rebuild from GET and keeps the actual core state', async () => {
  const { view } = await loadTun()
  patch.mockResolvedValue({})
  get.mockResolvedValueOnce({ data: stackConfig(false, 'Mixed') })
  expect(await view.updateTunStack('mixed')).toBe(false)
  expect(view.tunState.value.enabled).toBe(false)
  expect(view.tunStack.value).toBe('mixed')
  get.mockResolvedValueOnce({ data: stackConfig(false, 'System') })
  expect(await view.updateTunStack('gvisor')).toBe(false)
  expect(view.tunStack.value).toBe('system')
})

it('releases the busy state after PATCH failure without changing confirmed stack or enable', async () => {
  const { view } = await loadTun()
  const failure = new Error('Write rejected')
  patch.mockRejectedValueOnce(failure)
  await expect(view.updateTunStack('mixed')).rejects.toBe(failure)
  expect(view.isTunUpdating.value).toBe(false)
  expect(view.tunStack.value).toBe('system')
  expect(view.tunState.value.enabled).toBe(true)
  expect(get).toHaveBeenCalledOnce()
})

it('requires a successful post-write GET rather than assuming PATCH confirmed the stack', async () => {
  const { view, state } = await loadTun()
  const failure = new Error('Read failed')
  patch.mockResolvedValueOnce({})
  get.mockRejectedValueOnce(failure)
  await expect(view.updateTunStack('mixed')).rejects.toBe(failure)
  expect(view.configStatus.value).toBe('error')
  expect(view.canShowTunStack.value).toBe(false)
  expect(view.isTunUpdating.value).toBe(false)
  expect(state.configs.value.tun).toEqual({ enable: true, stack: 'System' })
})

it('rejects an obsolete post-write GET even after the new session has loaded the same backend UUID', async () => {
  const { view, host, state } = await loadTun()
  let finishOldGet!: (value: unknown) => void
  patch.mockResolvedValueOnce({})
  get.mockImplementationOnce(() => new Promise((resolve) => { finishOldGet = resolve }))
  const write = view.updateTunStack('mixed')
  await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(2))
  host.runtime({ processId: 2, runtimeEpoch: 2 })
  state.resetConfigs()
  get.mockResolvedValueOnce({ data: stackConfig(false, 'gVisor') })
  await state.fetchConfigs(true)
  finishOldGet({ data: stackConfig(true, 'Mixed') })
  expect(await write).toBeUndefined()
  expect(view.tunStack.value).toBe('gvisor')
  expect(view.tunState.value.enabled).toBe(false)
})

it('does not confirm an old write or let its finally clear a restarted session write', async () => {
  const { view, host, state } = await loadTun()
  let finishOld!: () => void
  let finishNew!: () => void
  patch
    .mockImplementationOnce(() => new Promise<void>((resolve) => { finishOld = resolve }))
    .mockImplementationOnce(() => new Promise<void>((resolve) => { finishNew = resolve }))
  const oldWrite = view.updateTunStack('mixed')
  host.runtime({ processId: 2, runtimeEpoch: 2 })
  state.resetConfigs()
  get.mockResolvedValueOnce({ data: stackConfig(false, 'gVisor') })
  await state.fetchConfigs(true)
  expect(view.isTunUpdating.value).toBe(false)
  const newWrite = view.updateTunStack('system')
  finishOld()
  expect(await oldWrite).toBeUndefined()
  expect(view.isTunUpdating.value).toBe(true)
  expect(view.tunStack.value).toBe('gvisor')
  get.mockResolvedValueOnce({ data: stackConfig(false, 'System') })
  finishNew()
  expect(await newWrite).toBe(true)
  expect(patch.mock.calls[1][0]).toEqual({ tun: { enable: false, stack: 'system' } })
})

it('blocks writes while the host operates or TUN enable is unknown', async () => {
  const { view, state, host } = await loadTun()
  host.runtime({ operation: 'restartCore' })
  await view.updateTunStack('mixed')
  await view.updateTunEnabled(false)
  expect(patch).not.toHaveBeenCalled()
  host.runtime({ operation: 'idle' })
  get.mockResolvedValueOnce({ data: { tun: { stack: 'System' } } })
  await state.fetchConfigs(true)
  expect(view.canShowTunStack.value).toBe(false)
  await view.updateTunStack('mixed')
  expect(patch).not.toHaveBeenCalled()
})

it('honors browser disableTunMode and blocks unready or failed API configurations', async () => {
  const { activeBackend, addBackend } = await import('@/store/setup')
  addBackend({ type: 'clash', protocol: 'http', host: '127.0.0.1', port: '9090', secondaryPath: '', password: '', disableTunMode: true })
  const state = await import('@/assembly/config')
  const { useBackendRuntimeConfig } = await import('@/composables/useBackendRuntimeConfig')
  const view = useBackendRuntimeConfig()
  await view.updateTunStack('mixed')
  expect(patch).not.toHaveBeenCalled()
  get.mockResolvedValueOnce({ data: stackConfig(true, 'System') })
  await state.fetchConfigs(true)
  expect(view.canShowTunStack.value).toBe(false)
  await view.updateTunStack('mixed')
  expect(patch).not.toHaveBeenCalled()
  activeBackend.value!.disableTunMode = false
  expect(view.canShowTunStack.value).toBe(true)
  get.mockRejectedValueOnce(new Error('Unauthorized'))
  await state.fetchConfigs(true).catch(() => {})
  expect(view.canShowTunStack.value).toBe(false)
  await view.updateTunStack('mixed')
  expect(patch).not.toHaveBeenCalled()
})

it('does not keep retrying unauthorized config requests', async () => {
  vi.useFakeTimers()
  await startMockHost()
  get.mockRejectedValue({ response: { status: 401 } })
  const state = await import('@/assembly/config')
  state.startConfigRuntime()
  await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(1))
  await vi.advanceTimersByTimeAsync(60000)
  expect(get).toHaveBeenCalledTimes(1)
  state.stopConfigRuntime()
})
