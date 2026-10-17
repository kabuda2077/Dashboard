import {
  commandHost,
  hasHostBridge,
  hostState,
  type RoutingProbeResult,
} from '@/composables/hostBridge'
import { backendSessionReady } from '@/helper/backendSession'
import {
  captureNetworkDiagnosticsTask,
  completeNetworkDiagnostic,
  registerNetworkDiagnostics,
  validRoutingProbeResult,
  type DiagnosticsRunOptions,
} from '@/helper/networkDiagnostics'
import { ref, type Ref } from 'vue'

export type RoutingResult = {
  status: 'idle' | 'checking' | 'observed' | 'unconfirmed' | 'unavailable' | 'failed'
  node?: string
  direct?: boolean
  reason?: string
}
export type RoutingTarget = {
  id: string
  name: string
  url: string
  region: 'domestic' | 'international'
  result: Ref<RoutingResult>
  checking: Ref<boolean>
}

export const routingTargets: RoutingTarget[] = [
  { id: 'alibaba', name: 'Alibaba', url: 'https://www.aliyun.com/robots.txt', region: 'domestic' },
  {
    id: 'bilibili',
    name: 'Bilibili',
    url: 'https://www.bilibili.com/robots.txt',
    region: 'domestic',
  },
  {
    id: 'youtube',
    name: 'YouTube',
    url: 'https://www.youtube.com/robots.txt',
    region: 'international',
  },
  {
    id: 'google',
    name: 'Google',
    url: 'https://www.google.com/robots.txt',
    region: 'international',
  },
  {
    id: 'telegram',
    name: 'Telegram',
    url: 'https://telegram.org/robots.txt',
    region: 'international',
  },
  {
    id: 'cloudflare',
    name: 'Cloudflare',
    url: 'https://www.cloudflare.com/cdn-cgi/trace',
    region: 'international',
  },
  {
    id: 'chatgpt',
    name: 'ChatGPT',
    url: 'https://chatgpt.com/robots.txt',
    region: 'international',
  },
  {
    id: 'github',
    name: 'GitHub',
    url: 'https://github.com/robots.txt',
    region: 'international',
  },
].map((target) => ({
  ...target,
  result: ref<RoutingResult>({ status: 'idle' }),
  checking: ref(false),
})) as RoutingTarget[]

export const isTestingRouting = ref(false)
type Batch = {
  targets: RoutingTarget[]
  promise: Promise<void>
  resolve: () => void
  isCurrent: () => boolean
  all: boolean
  shouldStart?: () => boolean
  manualIds: Set<string>
}
const tasks = new Map<string, Batch>()
let queue: Batch[] = []
let running: Batch | undefined
const displayResult = (observed: RoutingProbeResult): RoutingResult =>
  observed.status === 'matched'
    ? {
        status: 'observed',
        direct: observed.direct,
        ...(observed.node ? { node: observed.node } : {}),
      }
    : { status: observed.status, ...(observed.reason ? { reason: observed.reason } : {}) }
const publish = (target: RoutingTarget, result: RoutingProbeResult) => {
  target.result.value = displayResult(result)
  completeNetworkDiagnostic(
    'routing',
    { id: target.id, result, testedAt: Date.now() },
    result.status === 'matched',
  )
}
// Covers a previous WebView's bounded probe without turning busy into evidence.
const BUSY_RETRY_DELAYS = [500, 1000, 2000, 3000, 5000, 5000]
const releaseTarget = (batch: Batch, target: RoutingTarget) => {
  if (tasks.get(target.id) === batch) {
    tasks.delete(target.id)
    target.checking.value = false
  }
}
const keepRequestedTargets = (batch: Batch) => {
  if (batch.shouldStart && !batch.shouldStart()) {
    batch.targets = batch.targets.filter((target) => {
      if (batch.manualIds.has(target.id)) return true
      releaseTarget(batch, target)
      return false
    })
  }
  isTestingRouting.value = tasks.size > 0
  return batch.targets.length > 0
}
const startNext = () => {
  if (running) return
  const batch = queue.shift()
  if (!batch) return
  running = batch
  void (async () => {
    try {
      for (let attempt = 0; ; attempt++) {
        if (!batch.isCurrent() || !keepRequestedTargets(batch)) return
        const { coreType, runtimeEpoch } = hostState.value
        if (!coreType || runtimeEpoch === undefined) return
        const response = await commandHost({
          type: 'testRouting',
          coreType,
          expectedRuntimeEpoch: runtimeEpoch,
          ...(batch.all && batch.targets.length === routingTargets.length
            ? {}
            : { targetIds: batch.targets.map((target) => target.id) }),
        })
        if (!batch.isCurrent()) return
        if (response.code === 'busy') {
          // Retain the same task and old display. Hidden automatic requests are
          // dropped before retry; manual requests retain their navigation lifetime.
          if (!keepRequestedTargets(batch) || attempt >= BUSY_RETRY_DELAYS.length) return
          await new Promise<void>((resolve) => setTimeout(resolve, BUSY_RETRY_DELAYS[attempt]))
          continue
        }
        const ids = new Set(batch.targets.map((target) => target.id))
        if (
          response.status !== 'completed' ||
          response.code !== 'routingTested' ||
          !Array.isArray(response.routing) ||
          response.routing.length !== ids.size ||
          !response.routing.every((entry) => validRoutingProbeResult(entry) && ids.delete(entry.id))
        )
          throw new Error('Invalid routing result')
        for (const target of batch.targets)
          publish(
            target,
            response.routing.find((entry) => entry.id === target.id)!,
          )
        return
      }
    } catch {
      if (batch.isCurrent())
        for (const target of batch.targets)
          publish(target, { id: target.id, status: 'unconfirmed' })
    } finally {
      for (const target of batch.targets) releaseTarget(batch, target)
      if (running === batch) {
        running = undefined
        startNext()
      }
      isTestingRouting.value = tasks.size > 0
      batch.resolve()
    }
  })()
}
export const getRouting = (
  targetIds?: string[],
  options: DiagnosticsRunOptions = {},
): Promise<void> => {
  if (!backendSessionReady.value) return Promise.resolve()
  const selected = routingTargets.filter((target) => !targetIds || targetIds.includes(target.id))
  if (!hasHostBridge) {
    for (const target of selected)
      publish(target, { id: target.id, status: 'unavailable', reason: 'hostRequired' })
    return Promise.resolve()
  }
  if (!hostState.value.coreType || hostState.value.runtimeEpoch === undefined)
    return Promise.resolve()
  const pending = new Set<Promise<void>>()
  const missing = selected.filter((target) => {
    const existing = tasks.get(target.id)
    if (!existing?.isCurrent()) return true
    if (!options.shouldStart) existing.manualIds.add(target.id)
    pending.add(existing.promise)
    return false
  })
  if (missing.length) {
    let resolve!: () => void
    const promise = new Promise<void>((done) => {
      resolve = done
    })
    const batch: Batch = {
      targets: missing,
      promise,
      resolve,
      isCurrent: captureNetworkDiagnosticsTask(
        'routing',
        missing.map((target) => target.id),
      ).isCurrent,
      all: !targetIds && missing.length === routingTargets.length,
      shouldStart: options.shouldStart,
      manualIds: new Set(options.shouldStart ? [] : missing.map((target) => target.id)),
    }
    for (const target of missing) {
      tasks.set(target.id, batch)
      target.checking.value = true
    }
    isTestingRouting.value = true
    queue.push(batch)
    pending.add(promise)
    startNext()
  }
  if (pending.size === 1) return [...pending][0]
  return Promise.all(pending).then(() => undefined)
}
registerNetworkDiagnostics('routing', {
  ids: routingTargets.map((target) => target.id),
  run: getRouting,
  restore: (entry) => {
    if ('result' in entry) {
      const target = routingTargets.find((target) => target.id === entry.id)
      if (target) target.result.value = displayResult(entry.result)
    }
  },
  reset: () => {
    for (const batch of queue) batch.resolve()
    queue = []
    // Keep the physical host command serialized, even across readiness loss.
    // Its guarded reply can no longer publish into this session.
    tasks.clear()
    isTestingRouting.value = false
    for (const target of routingTargets) {
      target.result.value = { status: 'idle' }
      target.checking.value = false
    }
  },
})
