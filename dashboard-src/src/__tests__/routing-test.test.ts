import { commandHost, type CommandResult, type RoutingProbeResult } from '@/composables/hostBridge'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import { getRouting, isTestingRouting, routingTargets } from '@/helper/routing-test'
import { beforeEach, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'

vi.mock('@/composables/hostBridge', async () => ({
  commandHost: vi.fn(),
  hasHostBridge: true,
  hostState: (await import('vue')).ref({ coreType: 'mihomo', runtimeEpoch: 1 }),
}))
vi.mock('@/helper/backendSession', async () => {
  const { ref } = await import('vue')
  return { backendSessionGeneration: ref(0), backendSessionReady: ref(true) }
})
const host = vi.mocked(commandHost)
const deferred = <T>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
const results = (patch: Partial<RoutingProbeResult> = {}): CommandResult => ({
  status: 'completed',
  code: 'routingTested',
  routing: routingTargets.map(({ id }) => ({
    id,
    status: 'matched',
    direct: false,
    node: 'Exit node',
    ...patch,
  })),
})
beforeEach(() => {
  vi.clearAllMocks()
  backendSessionGeneration.value++
  backendSessionReady.value = true
})

it('manually requests one shared host task across navigation and maps exact exits', async () => {
  expect(routingTargets.map(({ id }) => id)).toEqual([
    'alibaba',
    'bilibili',
    'youtube',
    'google',
    'telegram',
    'cloudflare',
    'chatgpt',
    'github',
  ])
  expect(host).not.toHaveBeenCalled()
  const pending = deferred<CommandResult>()
  host.mockReturnValueOnce(pending.promise)
  const scope = effectScope()
  const task = scope.run(getRouting)!
  scope.stop()
  expect(getRouting()).toBe(task)
  expect(isTestingRouting.value).toBe(true)
  expect(routingTargets.every(({ result }) => result.value.status === 'checking')).toBe(true)
  expect(host).toHaveBeenCalledExactlyOnceWith({
    type: 'testRouting',
    coreType: 'mihomo',
    expectedRuntimeEpoch: 1,
  })
  const response = results()
  response.routing![0] = { id: 'alibaba', status: 'matched', direct: true }
  pending.resolve(response)
  await task
  expect(routingTargets[0].result.value).toEqual({ status: 'observed', direct: true })
  for (const target of routingTargets.slice(1))
    expect(target.result.value).toEqual({ status: 'observed', direct: false, node: 'Exit node' })
  expect(isTestingRouting.value).toBe(false)
})

it('keeps missing inbound, authentication, failed and unconfirmed evidence distinct', async () => {
  const response = results()
  response.routing![0] = { id: 'alibaba', status: 'unavailable', reason: 'noProxyInbound' }
  response.routing![1] = {
    id: 'bilibili',
    status: 'unavailable',
    reason: 'proxyAuthenticationRequired',
  }
  response.routing![2] = { id: 'youtube', status: 'failed' }
  response.routing![3] = { id: 'google', status: 'unconfirmed' }
  host.mockResolvedValueOnce(response)
  await getRouting()
  expect(routingTargets.slice(0, 4).map(({ result }) => result.value)).toEqual([
    { status: 'unavailable', reason: 'noProxyInbound' },
    { status: 'unavailable', reason: 'proxyAuthenticationRequired' },
    { status: 'failed' },
    { status: 'unconfirmed' },
  ])
})

it.each(['duplicate', 'missing', 'invalid-exit'] as const)(
  'rejects malformed %s evidence rather than guessing a node',
  async (kind) => {
    const response = results()
    if (kind === 'duplicate') response.routing![1].id = 'alibaba'
    if (kind === 'missing') response.routing!.pop()
    if (kind === 'invalid-exit') response.routing![0].direct = undefined
    host.mockResolvedValueOnce(response)
    await getRouting()
    expect(routingTargets.every(({ result }) => result.value.status === 'unconfirmed')).toBe(true)
  },
)

it('discards stale success and stale finally while a new session task is running', async () => {
  const old = deferred<CommandResult>(),
    current = deferred<CommandResult>()
  host.mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
  const first = getRouting()
  backendSessionGeneration.value++
  expect(routingTargets.every(({ result }) => result.value.status === 'idle')).toBe(true)
  const second = getRouting()
  old.resolve(results())
  await first
  expect(isTestingRouting.value).toBe(true)
  expect(routingTargets.every(({ result }) => result.value.status === 'checking')).toBe(true)
  current.resolve(results({ status: 'unconfirmed', direct: undefined, node: undefined }))
  await second
  expect(isTestingRouting.value).toBe(false)
  expect(routingTargets.every(({ result }) => result.value.status === 'unconfirmed')).toBe(true)
})

it('clears on readiness loss and ignores a late failed reply', async () => {
  const pending = deferred<CommandResult>()
  host.mockReturnValueOnce(pending.promise)
  const task = getRouting()
  backendSessionReady.value = false
  pending.resolve({ status: 'failed', code: 'operationFailed' })
  await task
  await getRouting()
  expect(host).toHaveBeenCalledTimes(1)
  expect(isTestingRouting.value).toBe(false)
  expect(routingTargets.every(({ result }) => result.value.status === 'idle')).toBe(true)
})
