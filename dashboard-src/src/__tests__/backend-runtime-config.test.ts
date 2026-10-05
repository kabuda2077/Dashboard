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
