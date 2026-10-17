import {
  commandHost,
  hasHostBridge,
  hostState,
  type RoutingProbeResult,
} from '@/composables/hostBridge'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import { ref, watch, type Ref } from 'vue'

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
})) as RoutingTarget[]

export const isTestingRouting = ref(false)
let task: { promise: Promise<void> } | undefined

// Shared across navigation; the host owns cancellation and socket disposal.
// A new generation/readiness state invalidates replies synchronously, including
// an old failure or finally block arriving while another test is running.
watch(
  [backendSessionGeneration, backendSessionReady],
  () => {
    task = undefined
    isTestingRouting.value = false
    for (const target of routingTargets) target.result.value = { status: 'idle' }
  },
  { flush: 'sync' },
)

const validResults = (value: unknown): value is RoutingProbeResult[] => {
  if (!Array.isArray(value) || value.length !== routingTargets.length) return false
  const ids = new Set(routingTargets.map((target) => target.id))
  return value.every((entry: unknown) => {
    if (!entry || typeof entry !== 'object') return false
    const result = entry as RoutingProbeResult
    if (!ids.delete(result.id)) return false
    if (!['matched', 'unconfirmed', 'unavailable', 'failed'].includes(result.status)) return false
    if (result.reason !== undefined && typeof result.reason !== 'string') return false
    if (result.status !== 'matched') return true
    return result.direct === true
      ? result.node === undefined
      : result.direct === false && typeof result.node === 'string' && !!result.node.trim()
  })
}

export const getRouting = (): Promise<void> => {
  if (task) return task.promise
  if (!backendSessionReady.value) return Promise.resolve()
  if (!hasHostBridge) {
    for (const target of routingTargets)
      target.result.value = { status: 'unavailable', reason: 'hostRequired' }
    return Promise.resolve()
  }
  const coreType = hostState.value.coreType
  const epoch = hostState.value.runtimeEpoch
  if (!coreType || epoch === undefined) return Promise.resolve()
  const current = { promise: Promise.resolve() }
  task = current
  isTestingRouting.value = true
  for (const target of routingTargets) target.result.value = { status: 'checking' }
  current.promise = (async () => {
    try {
      const result = await commandHost({
        type: 'testRouting',
        coreType,
        expectedRuntimeEpoch: epoch,
      })
      if (task !== current) return
      if (
        result.status !== 'completed' ||
        result.code !== 'routingTested' ||
        !validResults(result.routing)
      )
        throw new Error('Invalid routing result')
      for (const target of routingTargets) {
        const observed = result.routing.find((entry) => entry.id === target.id)!
        target.result.value =
          observed.status === 'matched'
            ? {
                status: 'observed',
                direct: observed.direct,
                ...(observed.node ? { node: observed.node } : {}),
              }
            : { status: observed.status, ...(observed.reason ? { reason: observed.reason } : {}) }
      }
    } catch {
      if (task === current)
        for (const target of routingTargets) target.result.value = { status: 'unconfirmed' }
    } finally {
      if (task === current) {
        task = undefined
        isTestingRouting.value = false
      }
    }
  })()
  return current.promise
}
