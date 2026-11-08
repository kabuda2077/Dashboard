import {
  getConnectionChains,
  getConnectionDownload,
  getConnectionHostname,
  getConnectionSourceIP,
  getConnectionUpload,
  getProcessFromConnection,
} from '@/helper'
import {
  clearConnectionHistoryFromIndexedDB,
  ConnectionHistoryType,
  getConnectionHistoryFromIndexedDB,
  saveConnectionHistoryToIndexedDB,
  type ConnectionHistoryData,
} from '@/helper/indexeddb'
import { registerPersistenceFlusher } from '@/helper/persistenceBarrier'
import type { Connection } from '@/types'
import ipaddr from 'ipaddr.js'
import { getDomain } from 'tldts'
import { shallowRef } from 'vue'
import { activeBackend } from './setup'

const uuid = () => activeBackend.value?.uuid || ''
const allHistoryTypes: ConnectionHistoryType[] = [
  ConnectionHistoryType.SourceIP,
  ConnectionHistoryType.Destination,
  ConnectionHistoryType.Process,
  ConnectionHistoryType.Outbound,
  ConnectionHistoryType.ProxyGroup,
]

type AggregationMaps = Record<ConnectionHistoryType, Map<string, ConnectionHistoryData>>
type HistorySnapshot = Record<ConnectionHistoryType, ConnectionHistoryData[]>

const createAggregationMaps = (): AggregationMaps => ({
  [ConnectionHistoryType.SourceIP]: new Map<string, ConnectionHistoryData>(),
  [ConnectionHistoryType.Destination]: new Map<string, ConnectionHistoryData>(),
  [ConnectionHistoryType.Process]: new Map<string, ConnectionHistoryData>(),
  [ConnectionHistoryType.Outbound]: new Map<string, ConnectionHistoryData>(),
  [ConnectionHistoryType.ProxyGroup]: new Map<string, ConnectionHistoryData>(),
})

const emptyView = (): Record<ConnectionHistoryType, ConnectionHistoryData[]> => ({
  [ConnectionHistoryType.SourceIP]: [],
  [ConnectionHistoryType.Destination]: [],
  [ConnectionHistoryType.Process]: [],
  [ConnectionHistoryType.Outbound]: [],
  [ConnectionHistoryType.ProxyGroup]: [],
})

let aggregationMaps = createAggregationMaps()
export const aggregatedDataMap = shallowRef(emptyView())

const VIEW_REFRESH_MS = 5_000
const FLUSH_EVERY_TICKS = 6
const TRIM_THRESHOLD = 2000
const TRIM_KEEP = 1500

let ready = false
let sessionUuid = ''
let sessionGeneration = 0
let initEpoch = 0
let lastClearEpoch = 0
let dirty = false
let viewTick = 0
const dirtyTypes = new Set<ConnectionHistoryType>()

interface InitContext {
  epoch: number
  uuid: string
  pending: AggregationMaps
  clear?: boolean
  task?: Promise<boolean>
}

let currentContext: InitContext | undefined
const pendingInitializations = new Set<InitContext>()
let persistenceQueue: Promise<void> = Promise.resolve()
const pendingSnapshots = new Map<string, HistorySnapshot>()

const enqueuePersistence = <T>(operation: () => Promise<T>) => {
  const result = persistenceQueue.then(operation)

  persistenceQueue = result.then(
    () => undefined,
    () => undefined,
  )
  return result
}

const markAllTypesDirty = () => {
  dirtyTypes.clear()
  for (const type of allHistoryTypes) {
    dirtyTypes.add(type)
  }
}

const refreshView = () => {
  if (!dirtyTypes.size) return

  const next = { ...aggregatedDataMap.value }
  for (const type of dirtyTypes) {
    next[type] = Array.from(aggregationMaps[type].values())
  }
  dirtyTypes.clear()
  aggregatedDataMap.value = next
}

const trimMap = (maps: AggregationMaps, type: ConnectionHistoryType) => {
  const map = maps[type]
  if (map.size <= TRIM_THRESHOLD) return false

  const kept = Array.from(map.values())
    .sort((a, b) => b.download - a.download)
    .slice(0, TRIM_KEEP)

  map.clear()
  for (const item of kept) {
    map.set(item.key, item)
  }
  return true
}

const snapshotMaps = (
  maps: AggregationMaps,
): Record<ConnectionHistoryType, ConnectionHistoryData[]> =>
  Object.fromEntries(
    allHistoryTypes.map((type) => [type, Array.from(maps[type].values(), (item) => ({ ...item }))]),
  ) as Record<ConnectionHistoryType, ConnectionHistoryData[]>

const saveSnapshot = async (
  targetUuid: string,
  snapshot: Record<ConnectionHistoryType, ConnectionHistoryData[]>,
) => {
  let success = true

  for (const type of allHistoryTypes) {
    try {
      await saveConnectionHistoryToIndexedDB(targetUuid, type, snapshot[type])
    } catch (error) {
      success = false
      console.error(`Failed to save connection history for ${type}:`, error)
    }
  }
  return success
}

const persistSnapshot = async (targetUuid: string, snapshot: HistorySnapshot) => {
  if (pendingSnapshots.get(targetUuid) !== snapshot) return true
  const success = await saveSnapshot(targetUuid, snapshot)
  if (success && pendingSnapshots.get(targetUuid) === snapshot) pendingSnapshots.delete(targetUuid)
  return success
}
const queueSnapshot = (targetUuid: string, snapshot: HistorySnapshot) => {
  pendingSnapshots.set(targetUuid, snapshot)
  return enqueuePersistence(() => persistSnapshot(targetUuid, snapshot))
}

const flushCurrentSession = () => {
  if (!sessionUuid || !dirty) return Promise.resolve()

  for (const type of allHistoryTypes) {
    if (trimMap(aggregationMaps, type)) dirtyTypes.add(type)
  }

  const targetUuid = sessionUuid
  const targetGeneration = sessionGeneration
  const snapshot = snapshotMaps(aggregationMaps)

  dirty = false
  return queueSnapshot(targetUuid, snapshot).then((success) => {
    if (!success && sessionUuid === targetUuid && sessionGeneration === targetGeneration) {
      dirty = true
    }
  })
}

registerPersistenceFlusher(async () => {
  await Promise.all([...pendingInitializations].map(initializeContext))
  await flushCurrentSession()
  await Promise.all(
    [...pendingSnapshots].map(([targetUuid, snapshot]) =>
      enqueuePersistence(() => persistSnapshot(targetUuid, snapshot)),
    ),
  )
  await persistenceQueue
  // Initialization or an incoming batch may have completed while awaiting disk.
  if (dirty) await flushCurrentSession()
  if (dirty || pendingSnapshots.size || pendingInitializations.size)
    throw new Error('Connection history has not been saved. Retry before closing.')
})

export const aggregateConnections = (
  connections: Connection[],
  type: ConnectionHistoryType,
): ConnectionHistoryData[] => {
  const map = new Map<string, ConnectionHistoryData>()

  connections.forEach((connection) => {
    let key = ''

    if (type === ConnectionHistoryType.SourceIP) {
      key = getConnectionSourceIP(connection)
    } else if (type === ConnectionHistoryType.Destination) {
      const host = getConnectionHostname(connection)
      key =
        ipaddr.IPv4.isValid(host) || ipaddr.IPv6.isValid(host)
          ? host
          : getDomain(host, { allowPrivateDomains: true }) || host
    } else if (type === ConnectionHistoryType.Process) {
      key = getProcessFromConnection(connection)
    } else if (type === ConnectionHistoryType.Outbound) {
      key = getConnectionChains(connection)[0] || '-'
    } else if (type === ConnectionHistoryType.ProxyGroup) {
      const chains = getConnectionChains(connection)
      key = chains[chains.length - 1] || '-'
    }

    const existing = map.get(key)
    if (existing) {
      existing.download += getConnectionDownload(connection)
      existing.upload += getConnectionUpload(connection)
      existing.count += 1
    } else {
      map.set(key, {
        key,
        download: getConnectionDownload(connection),
        upload: getConnectionUpload(connection),
        count: 1,
      })
    }
  })

  return Array.from(map.values())
}

const accumulateItems = (
  map: Map<string, ConnectionHistoryData>,
  items: Iterable<ConnectionHistoryData>,
) => {
  for (const item of items) {
    const existing = map.get(item.key)
    if (existing) {
      existing.download += item.download
      existing.upload += item.upload
      existing.count += item.count
    } else {
      map.set(item.key, { ...item })
    }
  }
}

const accumulateInto = (maps: AggregationMaps, connections: Connection[]) => {
  for (const type of allHistoryTypes) {
    accumulateItems(maps[type], aggregateConnections(connections, type))
  }
}

const mergePending = (maps: AggregationMaps, pending: AggregationMaps) => {
  for (const type of allHistoryTypes) {
    accumulateItems(maps[type], pending[type].values())
    trimMap(maps, type)
  }
}

const accumulateCurrent = (connections: Connection[]) => {
  accumulateInto(aggregationMaps, connections)
  for (const type of allHistoryTypes) trimMap(aggregationMaps, type)
  markAllTypesDirty()
  dirty = true
}

const loadHistoryMaps = async (targetUuid: string) => {
  const maps = createAggregationMaps()
  // A failed commit is newer than disk, even after leaving this session.
  const snapshot = pendingSnapshots.get(targetUuid)

  for (const type of allHistoryTypes) {
    let data = snapshot?.[type] ?? (await getConnectionHistoryFromIndexedDB(targetUuid, type))
    if (data.length > TRIM_THRESHOLD) {
      data = data.sort((a, b) => b.download - a.download).slice(0, TRIM_KEEP)
      await saveConnectionHistoryToIndexedDB(targetUuid, type, data)
    }
    for (const item of data) maps[type].set(item.key, { ...item })
  }
  return maps
}

const resetCurrentSession = (targetUuid: string) => {
  ready = false
  sessionUuid = targetUuid
  sessionGeneration++
  aggregationMaps = createAggregationMaps()
  dirty = false
  viewTick = 0
  markAllTypesDirty()
  aggregatedDataMap.value = emptyView()
}

const hasPending = (context: InitContext) =>
  allHistoryTypes.some((type) => context.pending[type].size > 0)

const settleStaleContext = async (context: InitContext, maps: AggregationMaps) => {
  if (!hasPending(context) || context.epoch < lastClearEpoch) return

  // Transfer ownership before awaiting a commit: a returning session must not
  // replay aggregates that are already represented by the pending snapshot.
  const pending = context.pending
  context.pending = createAggregationMaps()
  if (currentContext && currentContext !== context && currentContext.uuid === context.uuid) {
    mergePending(currentContext.pending, pending)
    return
  }

  mergePending(maps, pending)
  await queueSnapshot(context.uuid, snapshotMaps(maps))
}

const initializeContext = (context: InitContext): Promise<boolean> => {
  if (context.task) return context.task

  context.task = (async () => {
    // A failed clear must finish before a newer initialization reads old disk data.
    const pendingClear = [...pendingInitializations].find(
      (item) => item.clear && item.epoch < context.epoch && item.epoch >= lastClearEpoch,
    )
    if (pendingClear && !(await initializeContext(pendingClear))) return false

    const loadedMaps = await enqueuePersistence(async () => {
      if (context.epoch < lastClearEpoch) return createAggregationMaps()
      if (!context.clear) return loadHistoryMaps(context.uuid)
      await clearConnectionHistoryFromIndexedDB()
      pendingSnapshots.clear()
      return createAggregationMaps()
    })

    if (context.epoch !== initEpoch) {
      await settleStaleContext(context, loadedMaps)
    } else {
      aggregationMaps = loadedMaps
      ready = true
      if (hasPending(context)) {
        mergePending(aggregationMaps, context.pending)
        dirty = true
      }
      if (currentContext === context) currentContext = undefined
      markAllTypesDirty()
      refreshView()
    }
    pendingInitializations.delete(context)
    return true
  })()
    .catch((error) => {
      // Connection streams start initialization without awaiting it. Keep the
      // aggregates for a later retry, while the close barrier reports failure.
      console.error('Failed to initialize connection history:', error)
      return false
    })
    .finally(() => {
      context.task = undefined
    })
  return context.task
}

const createInitContext = (clear = false): InitContext => {
  const context: InitContext = {
    epoch: ++initEpoch,
    uuid: uuid(),
    pending: createAggregationMaps(),
    clear,
  }
  if (clear) lastClearEpoch = context.epoch
  for (const previous of pendingInitializations) {
    if (previous.epoch < lastClearEpoch) {
      pendingInitializations.delete(previous)
    } else if (previous.uuid === context.uuid) {
      mergePending(context.pending, previous.pending)
      previous.pending = createAggregationMaps()
      // Keep a clear as a prerequisite until its disk operation succeeds.
      if (!previous.clear) pendingInitializations.delete(previous)
    } else if (!previous.clear && !hasPending(previous)) {
      pendingInitializations.delete(previous)
    }
  }
  currentContext = context
  pendingInitializations.add(context)
  resetCurrentSession(context.uuid)
  return context
}

setInterval(() => {
  if (!ready) return

  if (!document.hidden) refreshView()
  if (++viewTick >= FLUSH_EVERY_TICKS) {
    viewTick = 0
    flushCurrentSession()
  }
}, VIEW_REFRESH_MS)

document.addEventListener('visibilitychange', () => {
  if (!ready) return

  if (document.hidden) flushCurrentSession()
  else refreshView()
})

window.addEventListener('pagehide', () => {
  if (ready) flushCurrentSession()
})

export const initAggregatedDataMap = async () => {
  flushCurrentSession()
  await initializeContext(createInitContext())
}

export const clearConnectionHistory = async () => {
  if (!(await initializeContext(createInitContext(true)))) {
    throw new Error('Connection history could not be cleared. Retry before closing.')
  }
}

export const mergeAggregatedData = (
  historical: ConnectionHistoryData[],
  newData: ConnectionHistoryData[],
): ConnectionHistoryData[] => {
  const map = new Map<string, ConnectionHistoryData>()

  for (const item of historical) map.set(item.key, item)
  for (const item of newData) {
    const existing = map.get(item.key)
    map.set(
      item.key,
      existing
        ? {
            key: existing.key,
            download: existing.download + item.download,
            upload: existing.upload + item.upload,
            count: existing.count + item.count,
          }
        : { ...item },
    )
  }

  return Array.from(map.values())
}

export const saveConnectionHistory = (newClosedConnections: Connection[]) => {
  if (!newClosedConnections.length) return

  const targetUuid = uuid()
  if (!ready || targetUuid !== sessionUuid) {
    if (currentContext?.uuid === targetUuid) {
      accumulateInto(currentContext.pending, newClosedConnections)
      for (const type of allHistoryTypes) trimMap(currentContext.pending, type)
    }
    return
  }

  accumulateCurrent(newClosedConnections)
}
