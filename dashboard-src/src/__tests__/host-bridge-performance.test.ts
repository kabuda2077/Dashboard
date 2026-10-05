import { beforeEach, expect, it, vi } from 'vitest'
import { makeHostSnapshot } from './hostFixture'

beforeEach(() => vi.resetModules())
it('applies runtime patches without replacing profiles and clears nullable PID', async () => {
  const bridge = await import('@/composables/hostBridge')
  bridge.applyHostState(makeHostSnapshot({ appVersion: '2.0.0', logText: 'existing' }))
  const profile = bridge.hostState.value.profiles!['mihomo']
  bridge.applyHostMessage({
    protocolVersion: 2,
    type: 'runtimeState',
    runtimeState: { processId: null, isRunning: false, runtimeEpoch: 2 },
  })
  expect(bridge.hostState.value).toMatchObject({
    processId: null,
    isRunning: false,
    runtimeEpoch: 2,
    appVersion: '2.0.0',
    logText: 'existing',
  })
  expect(bridge.hostState.value.profiles!['mihomo']).toBe(profile)
})
it('late metadata changes do not invent a new connection epoch', async () => {
  const bridge = await import('@/composables/hostBridge')
  bridge.applyHostState(makeHostSnapshot({ coreType: 'sing-box', runtimeEpoch: 7 }))
  bridge.applyHostRuntimeState({ coreVersion: 'sing-box 1.2.3' })
  expect(bridge.hostState.value.coreVersion).toBe('sing-box 1.2.3')
  expect(bridge.hostSessionGeneration.value).toBe(7)
})
it('updates icons and logs through separate incremental payloads', async () => {
  const bridge = await import('@/composables/hostBridge')
  bridge.applyHostState(makeHostSnapshot({ logText: 'first\n' }))
  bridge.applyHostMessage({
    protocolVersion: 2,
    type: 'iconCacheUpdated',
    iconCacheMap: { remote: 'local' },
  })
  bridge.applyHostMessage({ protocolVersion: 2, type: 'logAppend', logText: 'second\n' })
  expect(bridge.hostIconCache.value).toEqual({ remote: 'local' })
  expect(bridge.hostState.value.logText).toBe('first\nsecond\n')
})
