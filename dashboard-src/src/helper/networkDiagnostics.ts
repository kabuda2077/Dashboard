import {
  commandHost,
  hasHostBridge,
  hostState,
  type NetworkDiagnostics,
  type RoutingProbeResult,
} from '@/composables/hostBridge'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import { onScopeDispose, ref, watch, type Ref } from 'vue'

export type DiagnosticsKind = 'latency' | 'routing'
type LatencyEntry = NetworkDiagnostics['latency'][number]
type RoutingEntry = NetworkDiagnostics['routing'][number]
type Entry = LatencyEntry | RoutingEntry
export type DiagnosticsRunOptions = { shouldStart?: () => boolean }
type Adapter = {
  ids: string[]
  reset: () => void
  restore: (entry: Entry) => void
  run: (ids: string[], options: DiagnosticsRunOptions) => Promise<void>
}
const adapters = new Map<DiagnosticsKind, Adapter>()
const knownIds: Record<DiagnosticsKind, string[]> = {
  latency: [
    'bilibili',
    'tencent',
    'taobao',
    'github',
    'cloudflare',
    'google',
    'telegram',
    'youtube',
  ],
  routing: [
    'alibaba',
    'bilibili',
    'youtube',
    'google',
    'telegram',
    'cloudflare',
    'chatgpt',
    'github',
  ],
}
const records = {
  latency: new Map<string, LatencyEntry>(),
  routing: new Map<string, RoutingEntry>(),
}
const completed = { latency: new Map<string, number>(), routing: new Map<string, number>() }
// A cache GET may fill untouched rows, but cannot replace a task's display or result.
const touched = { latency: new Set<string>(), routing: new Set<string>() }
const retryAfter = { latency: new Map<string, number>(), routing: new Map<string, number>() }
const pages = new Set<{ kind: DiagnosticsKind; active: Ref<boolean | undefined> }>()
const MAX_AGE = 5 * 60_000
const contextGeneration = ref(0)
let session = 0
let pathGeneration = 0
let pathKey = ''
let contextReady = false
let contextStarted = false
let autoLatency = true
let hydrated = false
let cacheInvalidated = false
let readinessInvalidated = false
let hydrating: Promise<void> | undefined
let refreshTimer: ReturnType<typeof setTimeout> | undefined
let saveTimer: ReturnType<typeof setTimeout> | undefined
let savedSnapshot = ''
let listening = false

export const registerNetworkDiagnostics = (kind: DiagnosticsKind, adapter: Adapter) => {
  adapters.set(kind, adapter)
  for (const entry of records[kind].values()) adapter.restore(entry)
}

// Tasks own their lifetime outside the page scope. A path change invalidates replies
// without clearing the last usable display; a session change also clears that display.
export const captureNetworkDiagnosticsTask = (kind?: DiagnosticsKind, ids: string[] = []) => {
  const capturedSession = session
  const capturedPath = pathGeneration
  if (kind) for (const id of ids) touched[kind].add(id)
  return { isCurrent: () => capturedSession === session && capturedPath === pathGeneration }
}
export const completeNetworkDiagnostic = (
  kind: DiagnosticsKind,
  entry: Entry,
  success: boolean,
) => {
  touched[kind].add(entry.id)
  if (kind === 'latency') records.latency.set(entry.id, entry as LatencyEntry)
  else records.routing.set(entry.id, entry as RoutingEntry)
  completed[kind].set(entry.id, pathGeneration)
  if (success) retryAfter[kind].delete(entry.id)
  else retryAfter[kind].set(entry.id, Date.now() + 30_000)
  scheduleSave()
}

const visible = () => document.visibilityState !== 'hidden'
const active = (kind: DiagnosticsKind) =>
  visible() && [...pages].some((page) => page.kind === kind && page.active.value !== false)
const scheduleRefresh = () => {
  clearTimeout(refreshTimer)
  if (!contextReady || !backendSessionReady.value || !visible() || !pages.size) return
  if ([...pages].some((page) => page.active.value !== false)) void hydrate()
  refreshTimer = setTimeout(() => {
    refreshTimer = undefined
    void refreshVisible()
  }, 2000)
}
export const markNetworkDiagnosticsDirty = () => {
  cacheInvalidated = true
  pathGeneration++
  retryAfter.latency.clear()
  retryAfter.routing.clear()
  scheduleSave()
  scheduleRefresh()
}

const snapshot = (): NetworkDiagnostics => ({
  pathKey,
  // Persist old display values too; freshness belongs to each row, including
  // invalidations (such as rule writes) that do not change the fingerprint.
  latency: [...records.latency.values()].map((entry) => ({
    ...entry,
    invalidated: completed.latency.get(entry.id) !== pathGeneration,
  })),
  routing: [...records.routing.values()].map((entry) => ({
    ...entry,
    invalidated: completed.routing.get(entry.id) !== pathGeneration,
  })),
})
const scheduleSave = () => {
  clearTimeout(saveTimer)
  if (!hasHostBridge || !contextReady || !hydrated || !backendSessionReady.value) return
  const capturedSession = session
  saveTimer = setTimeout(() => {
    saveTimer = undefined
    if (capturedSession !== session || !backendSessionReady.value || !contextReady) return
    const networkDiagnostics = snapshot()
    const serialized = JSON.stringify(networkDiagnostics)
    if (serialized === savedSnapshot) return
    const { coreType, runtimeEpoch } = hostState.value
    if (!coreType || runtimeEpoch === undefined) return
    savedSnapshot = serialized
    void commandHost({
      type: 'setNetworkDiagnostics',
      coreType,
      expectedRuntimeEpoch: runtimeEpoch,
      networkDiagnostics,
    })
      .then((result) => {
        if (capturedSession === session && result.status !== 'completed') savedSnapshot = ''
      })
      .catch(() => {
        if (capturedSession === session) savedSnapshot = ''
      })
  }, 400)
}

export const validRoutingProbeResult = (value: unknown): value is RoutingProbeResult => {
  if (!value || typeof value !== 'object') return false
  const result = value as RoutingProbeResult
  if (!['matched', 'unconfirmed', 'unavailable', 'failed'].includes(result.status)) return false
  if (result.reason !== undefined && typeof result.reason !== 'string') return false
  if (result.status !== 'matched') return true
  return result.direct === true
    ? result.node === undefined
    : result.direct === false && typeof result.node === 'string' && !!result.node.trim()
}
const validTime = (time: number) => Number.isFinite(time) && time > 0 && time <= Date.now()
const restore = (cache: NetworkDiagnostics, sameGeneration: boolean) => {
  if (typeof cache.pathKey !== 'string' || cache.pathKey.length > 65536) return
  if (!Array.isArray(cache.latency) || !Array.isArray(cache.routing)) return
  if (cache.latency.length > 8 || cache.routing.length > 8) return
  const samePath = sameGeneration && !cacheInvalidated && cache.pathKey === pathKey
  for (const kind of ['latency', 'routing'] as const) {
    const seen = new Set<string>()
    for (const entry of cache[kind]) {
      if (!entry || !knownIds[kind].includes(entry.id) || seen.has(entry.id)) continue
      seen.add(entry.id)
      const protectedTarget = touched[kind].has(entry.id)
      if (!validTime(entry.testedAt) || (protectedTarget && records[kind].has(entry.id))) continue
      if (entry.invalidated !== undefined && typeof entry.invalidated !== 'boolean') continue
      if (kind === 'latency') {
        const samples = (entry as LatencyEntry).samples
        if (
          !Array.isArray(samples) ||
          samples.length !== 5 ||
          !samples.every((sample) => Number.isFinite(sample) && sample >= 0)
        )
          continue
        records.latency.set(entry.id, entry as LatencyEntry)
      } else {
        const result = (entry as RoutingEntry).result
        if (!validRoutingProbeResult(result) || result.id !== entry.id) continue
        records.routing.set(entry.id, entry as RoutingEntry)
      }
      // A protected target without a complete result still needs its historical
      // row in host memory. Keep it stale without replacing progressive samples.
      if (!protectedTarget) adapters.get(kind)?.restore(entry)
      const fresh = !protectedTarget && samePath && !entry.invalidated
      completed[kind].set(entry.id, fresh ? pathGeneration : -1)
      const successful =
        kind === 'latency'
          ? (entry as LatencyEntry).samples.some((sample) => sample > 0)
          : (entry as RoutingEntry).result.status === 'matched'
      if (fresh && !successful) retryAfter[kind].set(entry.id, entry.testedAt + 30_000)
    }
  }
}
const hydrate = (): Promise<void> => {
  if (hydrated) return Promise.resolve()
  if (hydrating) return hydrating
  if (!contextReady || !backendSessionReady.value) return Promise.resolve()
  if (!hasHostBridge) {
    hydrated = true
    return Promise.resolve()
  }
  const { coreType, runtimeEpoch } = hostState.value
  if (!coreType || runtimeEpoch === undefined) return Promise.resolve()
  const capturedSession = session
  const capturedPath = pathGeneration
  const pending = (async () => {
    try {
      const result = await commandHost({
        type: 'getNetworkDiagnostics',
        coreType,
        expectedRuntimeEpoch: runtimeEpoch,
      })
      if (capturedSession !== session) return
      if (result.status === 'completed' && result.networkDiagnostics) {
        if (!readinessInvalidated)
          restore(result.networkDiagnostics, capturedPath === pathGeneration)
        savedSnapshot = JSON.stringify(result.networkDiagnostics)
      } else if (result.status === 'completed' && !result.networkDiagnostics) {
        // A task may have completed while GET was pending; its result still needs saving.
        savedSnapshot = JSON.stringify({ pathKey, latency: [], routing: [] })
      }
    } catch {
      /* Cache availability does not block diagnostics. */
    } finally {
      if (capturedSession === session) {
        hydrated = true
        hydrating = undefined
        scheduleSave()
      }
    }
  })()
  hydrating = pending
  return pending
}
const refreshVisible = async () => {
  const capturedSession = session
  await hydrate()
  if (capturedSession !== session || !contextReady || !backendSessionReady.value) return
  for (const kind of ['latency', 'routing'] as const) {
    if (!active(kind) || (kind === 'latency' && !autoLatency)) continue
    const adapter = adapters.get(kind)
    if (!adapter) continue
    const now = Date.now()
    const ids = adapter.ids.filter((id) => {
      if ((retryAfter[kind].get(id) ?? 0) > now) return false
      const entry = records[kind].get(id)
      return (
        !entry ||
        completed[kind].get(id) !== pathGeneration ||
        (kind === 'latency' && now - entry.testedAt > MAX_AGE) ||
        retryAfter[kind].has(id)
      )
    })
    if (ids.length)
      void adapter.run(ids, {
        shouldStart: () => contextReady && backendSessionReady.value && active(kind),
      })
  }
}

// Loaded lazily from the page hook: api/clash can notify writes without importing
// config/proxy assembly modules during their own initialization.
const startContext = () => {
  if (contextStarted) return
  contextStarted = true
  void Promise.all([
    import('@/assembly/config'),
    import('@/assembly/proxies'),
    import('@/store/settings'),
    import('@/store/setup'),
  ]).then(([config, proxies, settings, setup]) => {
    watch(
      settings.autoConnectionCheck,
      (value) => {
        autoLatency = value
        scheduleRefresh()
      },
      { immediate: true },
    )
    const fingerprint = () => {
      if (
        !backendSessionReady.value ||
        !config.configsLoaded.value ||
        config.configsLoadedBackendUuid.value !== (setup.activeUuid.value ?? '') ||
        !Object.keys(proxies.proxyMap.value).length
      )
        return ''
      const cfg = config.configs.value
      const groups = Object.entries(proxies.proxyMap.value)
        .filter(([, proxy]) => proxy.now !== undefined || proxy.all !== undefined)
        .map(([name, proxy]) => [name, proxy.type, proxy.now ?? ''])
        .sort((a, b) => String(a[0]).localeCompare(String(b[0])))
      const key = JSON.stringify({
        mode: cfg.mode,
        tun: cfg.tun.enable,
        ipv6: cfg.ipv6,
        networkRevision: hostState.value.networkRevision ?? 0,
        groups,
      })
      // Keep large installations inside the bridge limit without adding credentials.
      if (key.length <= 65536) return key
      let a = 2166136261,
        b = 5381
      for (let i = 0; i < key.length; i++) {
        a = Math.imul(a ^ key.charCodeAt(i), 16777619)
        b = Math.imul(b, 33) ^ key.charCodeAt(i)
      }
      return JSON.stringify({ length: key.length, hash: [a >>> 0, b >>> 0] })
    }
    watch(
      [contextGeneration, fingerprint],
      ([, key]) => {
        contextReady = !!key
        if (!key) return
        if (pathKey && pathKey !== key) markNetworkDiagnosticsDirty()
        pathKey = key
        scheduleRefresh()
      },
      { immediate: true, flush: 'post' },
    )
  })
}
watch(
  [
    backendSessionGeneration,
    backendSessionReady,
    () => hostState.value.coreType,
    () => hostState.value.runtimeEpoch,
  ],
  (current, previous) => {
    if (previous && [0, 2, 3].some((index) => current[index] !== previous[index]))
      readinessInvalidated = false
    if (previous?.[1] === true && current[1] === false) readinessInvalidated = true
    session++
    contextGeneration.value++
    pathGeneration++
    pathKey = ''
    contextReady = false
    hydrated = false
    cacheInvalidated = readinessInvalidated
    hydrating = undefined
    savedSnapshot = ''
    clearTimeout(refreshTimer)
    clearTimeout(saveTimer)
    for (const kind of ['latency', 'routing'] as const) {
      records[kind].clear()
      completed[kind].clear()
      touched[kind].clear()
      retryAfter[kind].clear()
      adapters.get(kind)?.reset()
    }
    scheduleRefresh()
  },
  { flush: 'sync' },
)
watch(
  () => hostState.value.networkRevision,
  (value, previous) => {
    if (previous !== undefined && value !== previous) markNetworkDiagnosticsDirty()
  },
  { flush: 'sync' },
)
const onVisibility = () => {
  if (!visible()) clearTimeout(refreshTimer)
  else scheduleRefresh()
}
const onPageShow = (event: PageTransitionEvent) => {
  if (event.persisted) markNetworkDiagnosticsDirty()
  scheduleRefresh()
}
const onOnline = () => markNetworkDiagnosticsDirty()
export const useNetworkDiagnosticsPage = (
  kind: DiagnosticsKind,
  activePage: Ref<boolean | undefined>,
): void => {
  const page = { kind, active: activePage }
  pages.add(page)
  startContext()
  if (!listening) {
    listening = true
    document.addEventListener('visibilitychange', onVisibility)
    window.addEventListener('online', onOnline)
    window.addEventListener('pageshow', onPageShow)
  }
  const stop = watch(activePage, scheduleRefresh, { immediate: true, flush: 'sync' })
  onScopeDispose(() => {
    stop()
    pages.delete(page)
    scheduleRefresh()
  })
}
