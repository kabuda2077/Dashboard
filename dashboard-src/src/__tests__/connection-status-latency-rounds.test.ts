import ConnectionStatus from '@/components/overview/ConnectionStatus.vue'
import { backendSessionGeneration } from '@/helper/backendSession'
import { markNetworkDiagnosticsDirty } from '@/helper/networkDiagnostics'
import { getLatency, isTestingLatency, LATENCY_ROUNDS, latencyTargets } from '@/helper/overview'
import en from '@/i18n/en'
import { autoConnectionCheck } from '@/store/settings'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'

const fixtures = vi.hoisted(() => ({
  bilibili: vi.fn(),
  tencent: vi.fn(),
  taobao: vi.fn(),
  github: vi.fn(),
  cloudflare: vi.fn(),
  google: vi.fn(),
  telegram: vi.fn(),
  youtube: vi.fn(),
}))
vi.mock('@/api/latency', () => ({
  LATENCY_URLS: Object.fromEntries(
    Object.keys(fixtures).map((id) => [id, `https://${id}.test/favicon.ico`]),
  ),
  getBilibiliLatencyAPI: fixtures.bilibili,
  getTencentLatencyAPI: fixtures.tencent,
  getTaobaoLatencyAPI: fixtures.taobao,
  getGithubLatencyAPI: fixtures.github,
  getCloudflareLatencyAPI: fixtures.cloudflare,
  getGoogleLatencyAPI: fixtures.google,
  getTelegramLatencyAPI: fixtures.telegram,
  getYouTubeLatencyAPI: fixtures.youtube,
}))
vi.mock('@/helper/backendSession', async () => {
  const { ref } = await import('vue')
  return {
    backendSessionGeneration: ref(1),
    backendSessionReady: ref(true),
    captureBackendSession: () => ({ isCurrent: () => true }),
  }
})

type PendingRequest = {
  id: string
  signal: AbortSignal
  resolve: (value: number) => void
  reject: (reason: Error) => void
}
let app: App | undefined
let root: HTMLDivElement
const activeRequests = new Set<PendingRequest>()
const activeTargets = () => new Set([...activeRequests].map(({ id }) => id))
let maxActiveRequests = 0
let maxActiveTargets = 0
let maxRequestsPerTarget = 0
const controlledRequest = (id: string, signal: AbortSignal, honorAbort = true) =>
  new Promise<number>((resolve, reject) => {
    const settle = (callback: () => void) => {
      activeRequests.delete(request)
      signal.removeEventListener('abort', abort)
      callback()
    }
    const request: PendingRequest = {
      id,
      signal,
      resolve: (value) => settle(() => resolve(value)),
      reject: (reason) => settle(() => reject(reason)),
    }
    const abort = () => request.reject(new DOMException('Aborted', 'AbortError'))
    activeRequests.add(request)
    maxActiveRequests = Math.max(maxActiveRequests, activeRequests.size)
    maxActiveTargets = Math.max(maxActiveTargets, activeTargets().size)
    maxRequestsPerTarget = Math.max(
      maxRequestsPerTarget,
      [...activeRequests].filter((pending) => pending.id === id).length,
    )
    if (honorAbort) {
      signal.addEventListener('abort', abort, { once: true })
      if (signal.aborted) abort()
    }
  })
const mount = () => {
  root = document.createElement('div')
  document.body.append(root)
  app = createApp(ConnectionStatus)
  app.use(createI18n({ legacy: false, locale: 'en', messages: { en } }))
  app.mount(root)
}
const remount = () => {
  app!.unmount()
  root.remove()
  mount()
}
const row = (id: string) => root.querySelector<HTMLButtonElement>(`[data-network-target=${id}]`)!
const bars = (id: string) => [...row(id).querySelectorAll<HTMLElement>('.network-latency-bar')]
const requestFor = (id: string) => {
  const request = [...activeRequests].find((request) => request.id === id)
  expect(request, `pending request for ${id}`).toBeDefined()
  return request!
}
const respond = async (id: string, value: number | Error) => {
  const target = latencyTargets.find((target) => target.id === id)!
  const count = target.progress.value!.length
  const request = requestFor(id)
  if (value instanceof Error) request.reject(value)
  else request.resolve(value)
  await vi.waitFor(() => expect(target.progress.value).toHaveLength(count + 1))
  await nextTick()
}
const finishRemaining = async (value: number) => {
  while (isTestingLatency.value) {
    await vi.waitFor(() => expect(activeRequests.size).toBeGreaterThan(0))
    await Promise.all([...activeRequests].map((request) => respond(request.id, value)))
  }
}

beforeEach(() => {
  backendSessionGeneration.value++
  autoConnectionCheck.value = false
  activeRequests.clear()
  maxActiveRequests = 0
  maxActiveTargets = 0
  maxRequestsPerTarget = 0
  for (const target of latencyTargets) {
    const mock = fixtures[target.id as keyof typeof fixtures]
    mock
      .mockReset()
      .mockImplementation((signal: AbortSignal) => controlledRequest(target.id, signal))
  }
  mount()
})
afterEach(async () => {
  backendSessionGeneration.value++
  // Even deliberately abort-insensitive requests must release their pool slots after a failure.
  for (const request of activeRequests) request.resolve(999)
  app?.unmount()
  app = undefined
  root.remove()
  await vi.waitFor(() => expect(activeRequests.size).toBe(0))
})

it('shares four target slots across single and whole tests, deduplicates queued work and refills immediately', async () => {
  expect(latencyTargets.map(({ id }) => id)).toEqual([
    'bilibili',
    'tencent',
    'taobao',
    'github',
    'cloudflare',
    'google',
    'telegram',
    'youtube',
  ])
  expect(LATENCY_ROUNDS).toBe(5)
  const single = getLatency(['bilibili'])
  const anotherSingle = getLatency(['tencent'])
  const all = getLatency()
  const duplicateQueued = getLatency(['cloudflare'])
  const duplicateRunning = getLatency(['bilibili'])
  expect(latencyTargets.every((target) => target.checking.value)).toBe(true)
  expect(latencyTargets.every((target) => target.progress.value?.length === 0)).toBe(true)
  await vi.waitFor(() => expect(activeRequests.size).toBe(4))
  await nextTick()
  expect([...activeRequests].map(({ id }) => id)).toEqual([
    'bilibili',
    'tencent',
    'taobao',
    'github',
  ])
  expect(root.querySelectorAll('.network-latency-bar-grow')).toHaveLength(0)
  for (const target of latencyTargets.slice(4)) {
    expect(activeTargets().has(target.id)).toBe(false)
    expect(row(target.id).disabled).toBe(true)
    expect(row(target.id).getAttribute('aria-busy')).toBe('true')
    expect(fixtures[target.id as keyof typeof fixtures]).not.toHaveBeenCalled()
  }
  for (let round = 0; round < LATENCY_ROUNDS; round++) {
    await respond('bilibili', 42)
    if (round < LATENCY_ROUNDS - 1) expect(fixtures.cloudflare).not.toHaveBeenCalled()
  }
  await vi.waitFor(() => expect(fixtures.cloudflare).toHaveBeenCalledTimes(1))
  expect([...activeRequests].map(({ id }) => id)).toEqual([
    'tencent',
    'taobao',
    'github',
    'cloudflare',
  ])
  for (const target of latencyTargets.slice(1, 4)) {
    expect(target.progress.value).toEqual([])
    expect(fixtures[target.id as keyof typeof fixtures]).toHaveBeenCalledTimes(1)
  }
  expect(activeTargets().has('bilibili')).toBe(false)
  expect(latencyTargets[0].checking.value).toBe(false)
  expect(latencyTargets[0].progress.value).toEqual([42, 42, 42, 42, 42])
  await finishRemaining(42)
  await Promise.all([single, anotherSingle, all, duplicateQueued, duplicateRunning])
  expect(maxActiveRequests).toBe(4)
  expect(maxActiveTargets).toBe(4)
  expect(maxRequestsPerTarget).toBe(1)
  for (const mock of Object.values(fixtures)) expect(mock).toHaveBeenCalledTimes(5)
  for (const target of latencyTargets) {
    expect(target.ref.value).toEqual([42, 42, 42, 42, 42])
    expect(target.checking.value).toBe(false)
  }
  expect(activeTargets().size).toBe(0)
})

it('shares the eight-target task across remounts without growing restored samples or restarting requests', async () => {
  root.querySelector('button')!.click()
  await vi.waitFor(() => expect(activeRequests.size).toBe(4))
  expect(root.querySelectorAll('[data-network-target]')).toHaveLength(8)
  expect(root.querySelectorAll('[data-network-target] [role=img] span')).toHaveLength(40)
  await respond('bilibili', 42)
  expect(latencyTargets[0].ref.value).toEqual([42])
  expect(bars('bilibili')[0].classList.contains('network-latency-bar-grow')).toBe(true)
  const attempts = latencyTargets.map((target) => target.attempt.value)
  const calls = Object.values(fixtures).map((mock) => mock.mock.calls.length)
  autoConnectionCheck.value = true
  remount()
  await nextTick()
  expect(root.querySelector('button')!.disabled).toBe(true)
  expect(row('bilibili').querySelectorAll('.network-latency-bar-grow')).toHaveLength(0)
  const shared = getLatency()
  expect(latencyTargets.map((target) => target.attempt.value)).toEqual(attempts)
  expect(Object.values(fixtures).map((mock) => mock.mock.calls.length)).toEqual(calls)
  await respond('bilibili', 42)
  expect(bars('bilibili')[0].classList.contains('network-latency-bar-grow')).toBe(false)
  expect(bars('bilibili')[1].classList.contains('network-latency-bar-grow')).toBe(true)
  await finishRemaining(42)
  await shared
  remount()
  await nextTick()
  expect(root.querySelectorAll('.network-latency-bar-grow')).toHaveLength(0)
  for (const mock of Object.values(fixtures)) expect(mock).toHaveBeenCalledTimes(5)
  for (const target of latencyTargets) expect(row(target.id).textContent).toContain('42ms')
})

it('grows failed first-test samples progressively while pending bars stay static', async () => {
  const task = getLatency(['bilibili', 'github'])
  await vi.waitFor(() => expect(activeRequests.size).toBe(2))
  expect(root.querySelectorAll('.network-latency-bar-grow')).toHaveLength(0)
  for (let round = 0; round < LATENCY_ROUNDS; round++) {
    await respond('bilibili', new Error('offline'))
    expect(latencyTargets[0].ref.value).toEqual(Array(round + 1).fill(0))
    expect(latencyTargets[0].progress.value).toEqual(Array(round + 1).fill(0))
    const failed = bars('bilibili')[round]
    expect(failed.classList.contains('bg-error')).toBe(true)
    expect(failed.classList.contains('network-latency-bar-grow')).toBe(true)
    expect(failed.style.height).toBe('5px')
    expect(
      bars('bilibili')
        .slice(round + 1)
        .some((bar) => bar.classList.contains('network-latency-bar-grow')),
    ).toBe(false)
  }
  expect(row('bilibili').querySelectorAll('.bg-error')).toHaveLength(5)
  expect(row('bilibili').textContent).toContain('--')
  expect(row('github').querySelector('[role=img]')?.getAttribute('aria-label')).toContain('0/0')
  expect(row('github').querySelectorAll('.bg-error')).toHaveLength(0)
  expect(row('github').querySelectorAll('.network-latency-bar-grow')).toHaveLength(0)
  expect(row('tencent').querySelectorAll('.network-latency-bar-grow')).toHaveLength(0)
  await finishRemaining(25)
  await task
})

it('discards an invalidated path reply without overwriting replacement progress or clearing its ownership', async () => {
  const target = latencyTargets[0]
  target.ref.value = [12, 12, 12, 12, 12]
  // A late transport callback may still arrive after its signal was aborted.
  fixtures.bilibili.mockImplementationOnce((signal: AbortSignal) =>
    controlledRequest(target.id, signal, false),
  )
  const old = getLatency([target.id])
  await vi.waitFor(() => expect(activeRequests.size).toBe(1))
  const stale = requestFor(target.id)
  const oldAttempt = target.attempt.value
  markNetworkDiagnosticsDirty()
  const replacement = getLatency([target.id])
  await vi.waitFor(() => expect(fixtures.bilibili).toHaveBeenCalledTimes(2))
  expect(stale.signal.aborted).toBe(true)
  expect(target.attempt.value).toBe(oldAttempt + 1)
  const fresh = [...activeRequests].find((request) => request !== stale)!
  fresh.resolve(32)
  await vi.waitFor(() => expect(target.progress.value).toEqual([32]))
  stale.resolve(999)
  await old
  expect(target.ref.value).toEqual([12, 12, 12, 12, 12])
  expect(target.progress.value).toEqual([32])
  expect(target.checking.value).toBe(true)
  expect(activeTargets().has(target.id)).toBe(true)
  expect(isTestingLatency.value).toBe(true)
  const duplicate = getLatency([target.id])
  expect(target.attempt.value).toBe(oldAttempt + 1)
  for (let round = 1; round < LATENCY_ROUNDS; round++) await respond(target.id, 32)
  await Promise.all([replacement, duplicate])
  expect(target.ref.value).toEqual([32, 32, 32, 32, 32])
  expect(target.progress.value).toEqual([32, 32, 32, 32, 32])
  expect(fixtures.bilibili).toHaveBeenCalledTimes(6)
  expect(isTestingLatency.value).toBe(false)

  const invalidated = getLatency([target.id])
  await vi.waitFor(() => expect(activeRequests.size).toBe(1))
  await respond(target.id, 25)
  markNetworkDiagnosticsDirty()
  requestFor(target.id).resolve(999)
  await invalidated
  expect(target.progress.value).toBeUndefined()
  expect(target.ref.value).toEqual([32, 32, 32, 32, 32])
  expect(target.checking.value).toBe(false)
})

it('cancels active and queued work on a session change and lets a new session use the pool', async () => {
  for (const target of latencyTargets) target.ref.value = [12, 12, 12, 12, 12]
  const old = getLatency()
  await vi.waitFor(() => expect(activeRequests.size).toBe(4))
  await respond('bilibili', 20)
  const staleRequests = [...activeRequests]
  backendSessionGeneration.value++
  for (const request of staleRequests) expect(request.signal.aborted).toBe(true)
  for (const target of latencyTargets) {
    expect(target.ref.value).toEqual([])
    expect(target.progress.value).toBeUndefined()
    expect(target.checking.value).toBe(false)
  }
  expect(activeTargets().size).toBe(0)
  expect(isTestingLatency.value).toBe(false)
  const replacement = getLatency(['cloudflare'])
  await old
  await vi.waitFor(() => expect(fixtures.cloudflare).toHaveBeenCalledTimes(1))
  for (const id of ['google', 'telegram', 'youtube'] as const) {
    expect(fixtures[id]).not.toHaveBeenCalled()
  }
  expect(fixtures.bilibili).toHaveBeenCalledTimes(2)
  for (const id of ['tencent', 'taobao', 'github'] as const) {
    expect(fixtures[id]).toHaveBeenCalledTimes(1)
  }
  for (const request of staleRequests) request.resolve(999)
  await nextTick()
  expect(latencyTargets[0].ref.value).toEqual([])
  expect(latencyTargets[4].progress.value).toEqual([])
  expect([...activeTargets()]).toEqual(['cloudflare'])
  await finishRemaining(33)
  await replacement
  expect(latencyTargets[4].ref.value).toEqual([33, 33, 33, 33, 33])
  expect(maxActiveRequests).toBeLessThanOrEqual(4)
  expect(maxActiveTargets).toBeLessThanOrEqual(4)
})

it('keeps the old median during growing replacements and recreates bars for an unchanged new attempt', async () => {
  const target = latencyTargets[0]
  target.ref.value = [10, 20]
  await nextTick()
  const attempt = target.attempt.value
  row(target.id).click()
  await vi.waitFor(() => expect(activeRequests.size).toBe(1))
  await nextTick()
  expect(target.attempt.value).toBe(attempt + 1)
  expect(target.progress.value).toEqual([])
  expect(row(target.id).querySelector('.network-latency-number')?.textContent).toBe('15')
  expect(
    bars(target.id).filter((bar) => bar.classList.contains('network-latency-bar-grow')),
  ).toHaveLength(0)
  row(target.id).click()
  const shared = getLatency([target.id])
  expect(fixtures.bilibili).toHaveBeenCalledTimes(1)
  const samples = [10, 0, 20, 40, 1000]
  for (const [index, value] of samples.entries()) {
    await respond(target.id, value)
    expect(target.progress.value).toEqual(samples.slice(0, index + 1))
    expect(bars(target.id)[index].classList.contains('network-latency-bar-grow')).toBe(true)
    if (index < LATENCY_ROUNDS - 1) {
      expect(target.ref.value).toEqual([10, 20])
      expect(row(target.id).querySelector('.network-latency-number')?.textContent).toBe('15')
      expect(
        row(target.id).querySelector('.network-latency-metric')?.getAttribute('aria-label'),
      ).toContain('2/2')
    }
  }
  await shared
  // The outlier affects the mean, not the successful-sample median.
  expect(row(target.id).textContent).toContain('30ms')
  expect(row(target.id).querySelector('[role=img]')?.getAttribute('aria-label')).toContain('4/5')
  expect(target.ref.value).toEqual(samples)
  expect(target.progress.value).toEqual(samples)
  const previousBars = bars(target.id)
  const retest = getLatency([target.id])
  await vi.waitFor(() => expect(activeRequests.size).toBe(1))
  await nextTick()
  expect(target.attempt.value).toBe(attempt + 2)
  expect(bars(target.id).every((bar, index) => bar !== previousBars[index])).toBe(true)
  expect(row(target.id).textContent).toContain('30ms')
  for (const [index, value] of samples.entries()) {
    await respond(target.id, value)
    expect(bars(target.id)[index].classList.contains('network-latency-bar-grow')).toBe(true)
  }
  await retest
  expect(fixtures.bilibili).toHaveBeenCalledTimes(10)
  expect(target.ref.value).toEqual(samples)
})
