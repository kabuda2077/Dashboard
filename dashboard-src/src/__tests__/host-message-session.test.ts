import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { makeHostSnapshot, startMockHost } from './hostFixture'
beforeEach(() => vi.resetModules())
afterEach(() => {
  window.dispatchEvent(new Event('pagehide'))
  Reflect.deleteProperty(window, 'chrome')
})
it('shares one native receiver and uses the authoritative epoch once for all subscribers', async () => {
  const host = await startMockHost()
  const first = vi.fn(),
    second = vi.fn()
  host.bridge.addHostMessageListener(first)
  host.bridge.addHostMessageListener(second)
  expect(host.add).toHaveBeenCalledTimes(1)
  host.setState(makeHostSnapshot({ runtimeEpoch: 1 }))
  host.runtime({ processId: 2, runtimeEpoch: 2 })
  expect(host.bridge.hostSessionGeneration.value).toBe(2)
  expect(first).toHaveBeenCalledTimes(2)
  expect(second).toHaveBeenCalledTimes(2)
})
it('closed documents reject pending work and cannot reinstall the receiver', async () => {
  const host = await startMockHost()
  const waiting = host.bridge
    .commandHost({ type: 'chooseCoreFile', coreType: 'mihomo' })
    .catch((error) => error)
  host.bridge.disposeHostBridge()
  expect(await waiting).toBeInstanceOf(Error)
  await expect(host.bridge.requestHost({ type: 'requestState' })).rejects.toThrow()
  expect(host.add).toHaveBeenCalledTimes(1)
})
