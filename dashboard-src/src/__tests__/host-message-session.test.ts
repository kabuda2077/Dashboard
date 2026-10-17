import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import routingWire from '../../../.tmp/reports/bridge-fixtures-v2/routing.json'
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
it('reads routing results through the real request receiver using the C# wire fixture', async () => {
  const host = await startMockHost()
  const waiting = host.bridge.commandHost({
    type: 'testRouting',
    coreType: 'mihomo',
    expectedRuntimeEpoch: 1,
  })
  const request = host.post.mock.calls.at(-1)![0]
  host.emit({ ...routingWire, requestId: request.requestId } as Parameters<typeof host.emit>[0])
  const result = await waiting
  expect(result.status).toBe('completed')
  expect(result.code).toBe('routingTested')
  expect(result.routing).toHaveLength(8)
  expect(result.routing![0]).toEqual({ id: 'alibaba', status: 'matched', direct: true })
  expect(result.routing![1]).toEqual({
    id: 'youtube',
    status: 'matched',
    node: 'observed-leaf',
    direct: false,
  })
  expect(host.bridge.hostSessionGeneration.value).toBe(1)
})

it('settles an obsolete reply without rolling shared state back to its older epoch', async () => {
  const host = await startMockHost()
  const waiting = host.bridge.commandHost({
    type: 'testRouting',
    coreType: 'mihomo',
    expectedRuntimeEpoch: 1,
  })
  const request = host.post.mock.calls.at(-1)![0]
  host.runtime({ runtimeEpoch: 2, coreType: 'sing-box', apiStatus: 'ready', secret: 'current' })
  host.emit({ ...routingWire, requestId: request.requestId } as Parameters<typeof host.emit>[0])
  await waiting
  host.runtime({ runtimeEpoch: 1, coreType: 'mihomo', secret: 'obsolete' })
  expect(host.bridge.hostSessionGeneration.value).toBe(2)
  expect(host.bridge.hostState.value.coreType).toBe('sing-box')
  expect(host.bridge.hostState.value.secret).toBe('current')
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
