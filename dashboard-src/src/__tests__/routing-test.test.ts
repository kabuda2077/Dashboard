import { commandHost, type CommandResult, type RoutingProbeResult } from '@/composables/hostBridge'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import * as diagnostics from '@/helper/networkDiagnostics'
import { markNetworkDiagnosticsDirty } from '@/helper/networkDiagnostics'
import { getRouting, isTestingRouting, routingTargets } from '@/helper/routing-test'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
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
  host.mockReset()
  backendSessionGeneration.value++
  backendSessionReady.value = true
})

afterEach(() => vi.useRealTimers())

it('retries busy as the same task without replacing or caching evidence and stops after its retry budget', async () => {
  vi.useFakeTimers()
  const completed = vi.spyOn(diagnostics, 'completeNetworkDiagnostic')
  const target = routingTargets.find(({ id }) => id === 'google')!
  target.result.value = { status: 'observed', direct: false, node: 'Cached exit' }
  const response = {
    status: 'completed',
    code: 'routingTested',
    routing: [{ id: 'google', status: 'matched', direct: true }],
  } as CommandResult
  host.mockResolvedValueOnce({ status: 'rejected', code: 'busy' }).mockResolvedValueOnce(response)
  const task = getRouting(['google'])
  expect(getRouting(['google'])).toBe(task)
  await vi.advanceTimersByTimeAsync(499)
  expect(host).toHaveBeenCalledTimes(1)
  expect(target.result.value).toEqual({ status: 'observed', direct: false, node: 'Cached exit' })
  expect(target.checking.value).toBe(true)
  expect(completed).not.toHaveBeenCalled()
  await vi.advanceTimersByTimeAsync(1)
  await task
  expect(target.result.value).toEqual({ status: 'observed', direct: true })
  expect(completed).toHaveBeenCalledTimes(1)
  host.mockClear().mockResolvedValue({ status: 'rejected', code: 'busy' })
  completed.mockClear()
  const blocked = getRouting(['google'])
  await vi.advanceTimersByTimeAsync(30_000)
  await blocked
  const attempts = host.mock.calls.length
  expect(attempts).toBeGreaterThan(1)
  expect(attempts).toBeLessThanOrEqual(8)
  expect(target.result.value).toEqual({ status: 'observed', direct: true })
  expect(completed).not.toHaveBeenCalled()
  expect(target.checking.value).toBe(false)
  await vi.advanceTimersByTimeAsync(30_000)
  expect(host).toHaveBeenCalledTimes(attempts)
})

it('drops hidden queued automatic targets while a manual join survives dequeue and busy retry', async () => {
  vi.useFakeTimers()
  const firstResponse = deferred<CommandResult>()
  const googleResponse: CommandResult = {
    status: 'completed',
    code: 'routingTested',
    routing: [{ id: 'google', status: 'matched', direct: true }],
  }
  host
    .mockReturnValueOnce(firstResponse.promise)
    .mockResolvedValueOnce({ status: 'rejected', code: 'busy' })
    .mockResolvedValueOnce(googleResponse)
  const first = getRouting(['alibaba'])
  let visible = true
  const automatic = getRouting(['google', 'youtube'], { shouldStart: () => visible })
  const manual = getRouting(['google'])
  expect(manual).toBe(automatic)
  visible = false
  firstResponse.resolve({ ...results(), routing: results().routing!.slice(0, 1) })
  await first
  expect(host).toHaveBeenNthCalledWith(2, {
    type: 'testRouting',
    coreType: 'mihomo',
    expectedRuntimeEpoch: 1,
    targetIds: ['google'],
  })
  expect(routingTargets.find(({ id }) => id === 'youtube')!.checking.value).toBe(false)
  expect(routingTargets.find(({ id }) => id === 'youtube')!.result.value.status).toBe('idle')
  await vi.advanceTimersByTimeAsync(500)
  await Promise.all([automatic, manual])
  expect(host).toHaveBeenCalledTimes(3)
  expect(host).toHaveBeenLastCalledWith({
    type: 'testRouting',
    coreType: 'mihomo',
    expectedRuntimeEpoch: 1,
    targetIds: ['google'],
  })
  expect(routingTargets.find(({ id }) => id === 'google')!.result.value).toEqual({
    status: 'observed',
    direct: true,
  })
  expect(isTestingRouting.value).toBe(false)
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
  expect(routingTargets.every(({ checking }) => checking.value)).toBe(true)
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
  expect(host).toHaveBeenCalledTimes(1)
  old.resolve(results())
  await first
  expect(isTestingRouting.value).toBe(true)
  expect(routingTargets.every(({ checking }) => checking.value)).toBe(true)
  current.resolve(results({ status: 'unconfirmed', direct: undefined, node: undefined }))
  await second
  expect(isTestingRouting.value).toBe(false)
  expect(routingTargets.every(({ result }) => result.value.status === 'unconfirmed')).toBe(true)
})

it('merges single targets into a whole test and serializes batches while preserving old results', async () => {
  const old = { status: 'observed', direct: true } as const
  routingTargets[0].result.value = old
  const one = deferred<CommandResult>(),
    rest = deferred<CommandResult>()
  host.mockReturnValueOnce(one.promise).mockReturnValueOnce(rest.promise)
  const single = getRouting(['alibaba'])
  expect(getRouting(['alibaba'])).toBe(single)
  const all = getRouting()
  expect(host).toHaveBeenCalledTimes(1)
  expect(routingTargets[0].result.value).toEqual(old)
  expect(routingTargets.every(({ checking }) => checking.value)).toBe(true)
  one.resolve({ ...results(), routing: results().routing!.slice(0, 1) })
  await single
  expect(host).toHaveBeenNthCalledWith(2, {
    type: 'testRouting',
    coreType: 'mihomo',
    expectedRuntimeEpoch: 1,
    targetIds: routingTargets.slice(1).map(({ id }) => id),
  })
  rest.resolve({ ...results(), routing: results().routing!.slice(1) })
  await all
  expect(routingTargets.every(({ checking }) => !checking.value)).toBe(true)
})

it('queues the replacement path behind an invalidated host request and ignores its late result', async () => {
  const old = deferred<CommandResult>(),
    current = deferred<CommandResult>()
  host.mockReturnValueOnce(old.promise).mockReturnValueOnce(current.promise)
  const first = getRouting(['google'])
  markNetworkDiagnosticsDirty()
  const second = getRouting(['google'])
  expect(host).toHaveBeenCalledTimes(1)
  old.resolve({ ...results(), routing: results().routing!.filter(({ id }) => id === 'google') })
  await first
  expect(host).toHaveBeenCalledTimes(2)
  expect(routingTargets.find(({ id }) => id === 'google')!.result.value.status).toBe('idle')
  expect(isTestingRouting.value).toBe(true)
  current.resolve({
    status: 'completed',
    code: 'routingTested',
    routing: [{ id: 'google', status: 'matched', direct: true }],
  })
  await second
  expect(routingTargets.find(({ id }) => id === 'google')!.result.value).toEqual({
    status: 'observed',
    direct: true,
  })
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
