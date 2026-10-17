import { configs, configsLoaded, configsLoadedBackendUuid } from '@/assembly/config'
import { proxyMap } from '@/assembly/proxies'
import {
  commandHost,
  hostState,
  type CommandResult,
  type NetworkDiagnostics,
} from '@/composables/hostBridge'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import {
  captureNetworkDiagnosticsTask,
  completeNetworkDiagnostic,
  markNetworkDiagnosticsDirty,
  registerNetworkDiagnostics,
  useNetworkDiagnosticsPage,
} from '@/helper/networkDiagnostics'
import { autoConnectionCheck } from '@/store/settings'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref, type EffectScope } from 'vue'

vi.mock('@/assembly/config', async () => {
  const { ref } = await import('vue')
  return {
    configs: ref({ mode: 'rule', tun: { enable: true }, ipv6: false }),
    configsLoaded: ref(false),
    configsLoadedBackendUuid: ref('fixture'),
  }
})
vi.mock('@/assembly/proxies', async () => ({ proxyMap: (await import('vue')).ref({}) }))
vi.mock('@/store/setup', async () => ({ activeUuid: (await import('vue')).ref('fixture') }))
vi.mock('@/store/settings', async () => ({ autoConnectionCheck: (await import('vue')).ref(true) }))
vi.mock('@/helper/backendSession', async () => {
  const { ref } = await import('vue')
  return { backendSessionGeneration: ref(0), backendSessionReady: ref(true) }
})
vi.mock('@/composables/hostBridge', async () => ({
  commandHost: vi.fn(),
  hasHostBridge: true,
  hostState: (await import('vue')).ref({ coreType: 'mihomo', runtimeEpoch: 7, networkRevision: 0 }),
}))
const host = vi.mocked(commandHost)
const scopes: EffectScope[] = []
const latencyActive = ref(true),
  routingActive = ref(true)
const restored = { latency: vi.fn(), routing: vi.fn() }
const resets = { latency: vi.fn(), routing: vi.fn() }
let routingFails = false
const latencyRun = vi.fn(async (ids: string[]) => {
  captureNetworkDiagnosticsTask('latency', ids)
  for (const id of ids)
    completeNetworkDiagnostic(
      'latency',
      {
        id,
        samples: [24, 24, 24, 24, 24],
        testedAt: Date.now(),
      },
      true,
    )
})
const routingRun = vi.fn(async (ids: string[]) => {
  captureNetworkDiagnosticsTask('routing', ids)
  for (const id of ids)
    completeNetworkDiagnostic(
      'routing',
      {
        id,
        result: routingFails
          ? { id, status: 'unconfirmed' }
          : { id, status: 'matched', direct: true },
        testedAt: Date.now(),
      },
      !routingFails,
    )
})
const path = () =>
  JSON.stringify({
    mode: configs.value.mode,
    tun: true,
    ipv6: false,
    networkRevision: hostState.value.networkRevision ?? 0,
    groups: [['Select', 'Selector', 'Exit']],
  })
const cache = (): NetworkDiagnostics => ({
  pathKey: path(),
  latency: [
    { id: 'bilibili', samples: [10, 10, 10, 10, 10], testedAt: Date.now(), invalidated: false },
  ],
  routing: [
    {
      id: 'google',
      result: { id: 'google', status: 'matched', direct: true },
      testedAt: Date.now(),
      invalidated: false,
    },
  ],
})
const readyContext = () => {
  configsLoaded.value = true
  proxyMap.value = {
    Select: { name: 'Select', type: 'Selector', now: 'Exit' },
  } as typeof proxyMap.value
}
const mount = () => {
  const scope = effectScope()
  scopes.push(scope)
  scope.run(() => {
    useNetworkDiagnosticsPage('latency', latencyActive)
    useNetworkDiagnosticsPage('routing', routingActive)
  })
}
const settle = async () => {
  // Includes the lazy imports, config watcher and hydration continuation.
  for (let i = 0; i < 20; i++) await Promise.resolve()
  await nextTick()
}
const advance = async (ms = 2000) => {
  await settle()
  await vi.advanceTimersByTimeAsync(ms)
  await settle()
}
const visibility = async (state: 'hidden' | 'visible') => {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(state)
  document.dispatchEvent(new Event('visibilitychange'))
  await settle()
}
beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-06-01T00:00:00Z'))
  vi.clearAllMocks()
  configsLoaded.value = false
  configsLoadedBackendUuid.value = 'fixture'
  configs.value.mode = 'rule'
  proxyMap.value = {}
  hostState.value = { coreType: 'mihomo', runtimeEpoch: 7, networkRevision: 0 }
  backendSessionReady.value = true
  backendSessionGeneration.value++
  autoConnectionCheck.value = true
  latencyActive.value = routingActive.value = true
  routingFails = false
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
  host.mockResolvedValue({ status: 'completed', code: 'ok' })
  registerNetworkDiagnostics('latency', {
    ids: ['bilibili'],
    reset: resets.latency,
    restore: restored.latency,
    run: latencyRun,
  })
  registerNetworkDiagnostics('routing', {
    ids: ['google'],
    reset: resets.routing,
    restore: restored.routing,
    run: routingRun,
  })
  resets.latency.mockClear()
  resets.routing.mockClear()
})
afterEach(() => {
  for (const scope of scopes.splice(0)) scope.stop()
  backendSessionGeneration.value++
  vi.useRealTimers()
  vi.restoreAllMocks()
})

it('waits for a real path before hydrating and reuses host memory across ordinary tray restores', async () => {
  const stored = cache()
  host.mockImplementation(async (command) => ({
    status: 'completed',
    code: 'ok',
    ...(command.type === 'getNetworkDiagnostics' ? { networkDiagnostics: stored } : {}),
  }))
  mount()
  await advance(3000)
  expect(host).not.toHaveBeenCalled()
  readyContext()
  await advance()
  expect(host).toHaveBeenCalledExactlyOnceWith({
    type: 'getNetworkDiagnostics',
    coreType: 'mihomo',
    expectedRuntimeEpoch: 7,
  })
  expect(restored.latency).toHaveBeenCalledWith(stored.latency[0])
  expect(restored.routing).toHaveBeenCalledWith(stored.routing[0])
  expect(latencyRun).not.toHaveBeenCalled()
  expect(routingRun).not.toHaveBeenCalled()
  await visibility('hidden')
  await advance(90_000)
  await visibility('visible')
  await advance()
  expect(latencyRun).not.toHaveBeenCalled()
  expect(routingRun).not.toHaveBeenCalled()
  await visibility('hidden')
  await advance(5 * 60_000)
  await visibility('visible')
  await advance()
  expect(latencyRun).toHaveBeenCalledExactlyOnceWith(['bilibili'], {
    shouldStart: expect.any(Function),
  })
  expect(routingRun).not.toHaveBeenCalled()
  await advance(6 * 60_000)
  expect(latencyRun).toHaveBeenCalledTimes(1) // No interval polling while visible.
})

it('debounces visible path changes and saves invalidated display rows for the next WebView', async () => {
  readyContext()
  mount()
  await advance()
  await advance(400)
  expect(latencyRun).toHaveBeenCalledTimes(1)
  expect(routingRun).toHaveBeenCalledTimes(1)
  routingActive.value = false
  configs.value.mode = 'global'
  await advance(1000)
  configs.value.mode = 'direct'
  await advance(1000)
  expect(latencyRun).toHaveBeenCalledTimes(1)
  await advance(1000)
  expect(latencyRun).toHaveBeenCalledTimes(2)
  expect(routingRun).toHaveBeenCalledTimes(1)
  await advance(400)
  const writes = host.mock.calls
    .map(([command]) => command)
    .filter((command) => command.type === 'setNetworkDiagnostics')
  expect(writes.at(-1)!.networkDiagnostics.routing).toEqual([
    expect.objectContaining({ id: 'google', invalidated: true }),
  ])
  expect(writes.at(-1)!.networkDiagnostics.latency).toEqual([
    expect.objectContaining({ id: 'bilibili', invalidated: false }),
  ])
  expect(resets.routing).not.toHaveBeenCalled()
  routingActive.value = true
  await advance()
  expect(routingRun).toHaveBeenCalledTimes(2)
  // Membership/history churn does not change the observed path.
  proxyMap.value.Select.all = ['One', 'Two']
  await advance()
  expect(routingRun).toHaveBeenCalledTimes(2)
})

it('honors auto latency off, retries failures briefly, and refreshes for online or host network recovery', async () => {
  autoConnectionCheck.value = false
  routingFails = true
  readyContext()
  mount()
  await advance()
  expect(latencyRun).not.toHaveBeenCalled()
  expect(routingRun).toHaveBeenCalledTimes(1)
  routingActive.value = false
  await advance(100)
  routingActive.value = true
  await advance()
  expect(routingRun).toHaveBeenCalledTimes(1)
  window.dispatchEvent(new Event('online'))
  await advance()
  expect(routingRun).toHaveBeenCalledTimes(2) // Path change bypasses failure cooldown.
  hostState.value.networkRevision = 1
  await advance()
  expect(routingRun).toHaveBeenCalledTimes(3)
  expect(latencyRun).not.toHaveBeenCalled()
  await advance(30_000)
  routingActive.value = false
  await settle()
  routingActive.value = true
  await advance()
  expect(routingRun).toHaveBeenCalledTimes(4)
  routingFails = false
  const restoredPage = new Event('pageshow')
  Object.defineProperty(restoredPage, 'persisted', { value: true })
  window.dispatchEvent(restoredPage)
  await advance()
  expect(routingRun).toHaveBeenCalledTimes(5)
})

it('merges untouched cache rows and preserves fallback history when a manual task is invalidated', async () => {
  let reply!: (result: CommandResult) => void
  const pending = new Promise<CommandResult>((resolve) => {
    reply = resolve
  })
  host.mockReturnValueOnce(pending)
  const stored = cache()
  readyContext()
  mount()
  await advance()
  expect(host).toHaveBeenCalledTimes(1)
  // One row is in flight and another has completed since the cache GET started.
  stored.latency.push(
    ...['tencent', 'github'].map((id) => ({
      id,
      samples: [10, 10, 10, 10, 10],
      testedAt: Date.now(),
      invalidated: false,
    })),
  )
  const old = captureNetworkDiagnosticsTask('latency', ['tencent'])
  await latencyRun(['bilibili'])
  reply({ status: 'completed', code: 'ok', networkDiagnostics: stored })
  await settle()
  expect(restored.latency).toHaveBeenCalledExactlyOnceWith(stored.latency[2])
  expect(restored.routing).toHaveBeenCalledExactlyOnceWith(stored.routing[0])
  expect(routingRun).not.toHaveBeenCalled() // Untouched fresh routing stays fresh.

  // A new cache GET is pending while a manual row starts, then loses its path.
  let lateReply!: (result: CommandResult) => void
  host.mockReturnValueOnce(
    new Promise((resolve) => {
      lateReply = resolve
    }),
  )
  backendSessionGeneration.value++
  await settle()
  expect(old.isCurrent()).toBe(false)
  const abandoned = captureNetworkDiagnosticsTask('latency', ['tencent'])
  await latencyRun(['bilibili'])
  latencyActive.value = routingActive.value = false
  markNetworkDiagnosticsDirty()
  expect(abandoned.isCurrent()).toBe(false)
  lateReply({ status: 'completed', code: 'ok', networkDiagnostics: stored })
  await advance(400)
  const writes = host.mock.calls
    .map(([command]) => command)
    .filter((command) => command.type === 'setNetworkDiagnostics')
  const saved = writes.at(-1)!.networkDiagnostics
  expect(saved.latency.find(({ id }) => id === 'tencent')).toEqual({
    ...stored.latency[1],
    invalidated: true,
  })
  expect(saved.latency.find(({ id }) => id === 'bilibili')!.samples).toEqual([24, 24, 24, 24, 24])
  expect(restored.latency.mock.calls.some(([entry]) => entry.id === 'tencent')).toBe(false)
})

it('keeps pre-hydration online invalidation when the host cache still has the same path key', async () => {
  const stored = cache()
  host.mockImplementation(async (command) => ({
    status: 'completed',
    code: 'ok',
    ...(command.type === 'getNetworkDiagnostics' ? { networkDiagnostics: stored } : {}),
  }))
  routingActive.value = false
  mount()
  await settle()
  window.dispatchEvent(new Event('online'))
  readyContext()
  await settle()
  // Cache display restoration is immediate; the detection debounce has not elapsed.
  expect(restored.routing).toHaveBeenCalledWith(stored.routing[0])
  expect(latencyRun).not.toHaveBeenCalled()
  await advance()
  await advance(400)
  expect(latencyRun).toHaveBeenCalledTimes(1)
  expect(routingRun).not.toHaveBeenCalled()
  const writes = host.mock.calls
    .map(([command]) => command)
    .filter((command) => command.type === 'setNetworkDiagnostics')
  const saved = writes.at(-1)!.networkDiagnostics
  expect(saved.routing).toEqual([{ ...stored.routing[0], invalidated: true }])
  // A rule/online invalidation can keep the same fingerprint. Reopening must
  // restore its old display without promoting it back to fresh.
  host.mockImplementation(async (command) => ({
    status: 'completed',
    code: 'ok',
    ...(command.type === 'getNetworkDiagnostics' ? { networkDiagnostics: saved } : {}),
  }))
  backendSessionGeneration.value++
  await settle()
  expect(restored.routing).toHaveBeenLastCalledWith(saved.routing[0])
  routingActive.value = true
  await advance()
  expect(routingRun).toHaveBeenCalledTimes(1)
})

it('ignores an old cache GET after the session changes while allowing the current cache to restore', async () => {
  let oldReply!: (result: CommandResult) => void
  let newReply!: (result: CommandResult) => void
  host
    .mockReturnValueOnce(
      new Promise((resolve) => {
        oldReply = resolve
      }),
    )
    .mockReturnValueOnce(
      new Promise((resolve) => {
        newReply = resolve
      }),
    )
  const stored = cache()
  readyContext()
  mount()
  await settle()
  expect(host).toHaveBeenCalledTimes(1)
  backendSessionGeneration.value++
  await settle()
  expect(host).toHaveBeenCalledTimes(2)
  oldReply({ status: 'completed', code: 'ok', networkDiagnostics: stored })
  await settle()
  expect(restored.latency).not.toHaveBeenCalled()
  newReply({ status: 'completed', code: 'ok', networkDiagnostics: stored })
  await settle()
  expect(restored.latency).toHaveBeenCalledExactlyOnceWith(stored.latency[0])
  await advance()
  expect(latencyRun).not.toHaveBeenCalled()
  expect(routingRun).not.toHaveBeenCalled()
})

it('refreshes after readiness loss even when the same epoch host still returns its old cache', async () => {
  const stored = cache()
  host.mockImplementation(async (command) => ({
    status: 'completed',
    code: 'ok',
    ...(command.type === 'getNetworkDiagnostics' ? { networkDiagnostics: stored } : {}),
  }))
  readyContext()
  mount()
  await advance()
  expect(restored.latency).toHaveBeenCalledTimes(1)
  expect(latencyRun).not.toHaveBeenCalled()
  backendSessionReady.value = false
  await settle()
  backendSessionReady.value = true
  await advance()
  expect(restored.latency).toHaveBeenCalledTimes(1)
  expect(latencyRun).toHaveBeenCalledTimes(1)
  expect(routingRun).toHaveBeenCalledTimes(1)
})

it('does not start automatic work after hide or page disposal but keeps shared invalidation', async () => {
  readyContext()
  mount()
  await advance(1000)
  await visibility('hidden')
  await advance()
  expect(latencyRun).not.toHaveBeenCalled()
  await visibility('visible')
  for (const scope of scopes.splice(0)) scope.stop()
  await advance()
  expect(latencyRun).not.toHaveBeenCalled()
  window.dispatchEvent(new Event('online'))
  markNetworkDiagnosticsDirty()
  mount()
  await advance()
  expect(latencyRun).toHaveBeenCalledTimes(1)
  expect(routingRun).toHaveBeenCalledTimes(1)
})
