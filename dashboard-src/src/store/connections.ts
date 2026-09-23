import {
  disconnectByIdAPI,
  fetchConnectionsAPI,
  getConnectionVisibleSearchValues,
} from '@/assembly/connections'
import { CONNECTION_TAB_TYPE, SORT_DIRECTION, SORT_TYPE } from '@/constant'
import {
  getChainsStringFromConnection,
  getConnectionDownload,
  getConnectionNetwork,
  getConnectionRule,
  getConnectionSourceIP,
  getConnectionStart,
  getConnectionUpload,
  getHostFromConnection,
  getInboundUserFromConnection,
  getNetworkTypeFromConnection,
} from '@/helper'
import { toSearchRegex } from '@/helper/search'
import type { Connection } from '@/types'
import { useDashboardStorage as useStorage } from '@/helper/storage'
import { watchOnce } from '@vueuse/core'
import dayjs from 'dayjs'
import { computed, ref, shallowRef, watch } from 'vue'
import { initAggregatedDataMap, saveConnectionHistory } from './connHistory'
import { activeUuid } from './setup'
import { captureBackendSession } from '@/helper/backendSession'
import {
  autoDisconnectIdleUDP,
  autoDisconnectIdleUDPTime,
  connectionCardLines,
  connectionTableColumns,
  isConnectionCard,
  proxyChainDirection,
  showFullProxyChain,
} from './settings'

export const connectionTabShow = ref(CONNECTION_TAB_TYPE.ACTIVE)
export const connectionSortType = useStorage<SORT_TYPE>(
  'config/connection-sort-type',
  SORT_TYPE.HOST,
)
export const connectionSortDirection = useStorage<SORT_DIRECTION>(
  'config/connection-sort-direction',
  SORT_DIRECTION.ASC,
)

export const quickFilterRegex = useStorage<string>('config/quick-filter-regex', 'direct|dns-out')
export const quickFilterEnabled = useStorage<boolean>('config/quick-filter-enabled', false)
export const connectionFilter = ref('')
export const sourceIPFilter = ref<string[] | null>(null)

export const activeConnections = shallowRef<Connection[]>([])
export const closedConnections = shallowRef<Connection[]>([])
export const activeConnectionCount = ref(0)
export const isPaused = ref(false)

export const downloadTotal = ref(0)
export const uploadTotal = ref(0)

let cancel: (() => void) | undefined
type ConnectionsMode = 'summary' | 'full'
let connectionMode: ConnectionsMode | null = null
let connectionBackendUuid: string | null = null

export const initConnections = (mode: ConnectionsMode = 'full') => {
  const backendUuid = activeUuid.value

  if (cancel && connectionMode === mode && connectionBackendUuid === backendUuid) {
    return
  }

  cancel?.()
  connectionMode = mode
  connectionBackendUuid = backendUuid
  activeConnections.value = []
  closedConnections.value = []
  activeConnectionCount.value = 0
  downloadTotal.value = 0
  uploadTotal.value = 0
  initAggregatedDataMap()
  const session = captureBackendSession()
  const ws = fetchConnectionsAPI()
  const unwatch = watch(ws.data, (snapshot) => {
    if (!snapshot || !session.isCurrent()) return

    if (snapshot.downloadTotal != null && snapshot.uploadTotal != null) {
      downloadTotal.value = snapshot.downloadTotal
      uploadTotal.value = snapshot.uploadTotal
    }
    activeConnectionCount.value = snapshot.active.length

    if (isPaused.value) {
      return
    }

    if (mode === 'summary') {
      return
    }

    activeConnections.value = snapshot.active
    activeConnectionCount.value = activeConnections.value.length

    if (snapshot.closed.length > 0) {
      closedConnections.value = closedConnections.value.concat(snapshot.closed).slice(-500)
      saveConnectionHistory(snapshot.closed)
    }
  })

  let stopIdleWatch: (() => void) | undefined
  if (autoDisconnectIdleUDP.value) {
    stopIdleWatch = watchOnce(activeConnections, () => {
      if (!session.isCurrent()) return
      activeConnections.value
        .filter((conn) => getConnectionNetwork(conn) !== 'tcp')
        .forEach((conn) => {
          const now = dayjs()
          const start = dayjs(getConnectionStart(conn))

          if (now.diff(start, 'minute') > autoDisconnectIdleUDPTime.value) {
            void disconnectByIdAPI(conn.id).catch(() => {})
          }
        })
    })
  }

  cancel = () => {
    stopIdleWatch?.()
    unwatch()
    ws.close()
  }
}

export const stopConnections = () => {
  cancel?.()
  cancel = undefined
  connectionMode = null
  connectionBackendUuid = null
}

export const resetConnections = () => {
  stopConnections()
  activeConnections.value = []
  closedConnections.value = []
  activeConnectionCount.value = 0
  downloadTotal.value = 0
  uploadTotal.value = 0
}

const isDesc = computed(() => {
  return connectionSortDirection.value === SORT_DIRECTION.DESC
})

const sortKeyFunctionMap: Record<SORT_TYPE, (connection: Connection) => string | number> = {
  [SORT_TYPE.HOST]: getHostFromConnection,
  [SORT_TYPE.RULE]: getConnectionRule,
  [SORT_TYPE.CHAINS]: getChainsStringFromConnection,
  [SORT_TYPE.DOWNLOAD]: getConnectionDownload,
  [SORT_TYPE.DOWNLOAD_SPEED]: (connection) => connection.downloadSpeed,
  [SORT_TYPE.UPLOAD]: getConnectionUpload,
  [SORT_TYPE.UPLOAD_SPEED]: (connection) => connection.uploadSpeed,
  [SORT_TYPE.SOURCE_IP]: getConnectionSourceIP,
  [SORT_TYPE.TYPE]: getNetworkTypeFromConnection,
  [SORT_TYPE.CONNECT_TIME]: (connection) => {
    const start = getConnectionStart(connection)
    if (typeof start === 'number') return start

    const parsed = Date.parse(start)
    return Number.isNaN(parsed) ? 0 : parsed
  },
  [SORT_TYPE.INBOUND_USER]: getInboundUserFromConnection,
}

export const connections = computed(() => {
  switch (connectionTabShow.value) {
    case CONNECTION_TAB_TYPE.ACTIVE:
      return activeConnections.value
    case CONNECTION_TAB_TYPE.CLOSED:
      return closedConnections.value
    case CONNECTION_TAB_TYPE.ALL:
      return closedConnections.value.concat(activeConnections.value)
    default:
      return activeConnections.value
  }
})

const closedConnectionIds = computed(() => new Set(closedConnections.value.map(({ id }) => id)))

export const isClosedConnection = (connection: Connection) =>
  closedConnectionIds.value.has(connection.id)

const filterConnections = (items: readonly Connection[]) => {
  const searchRegex = toSearchRegex(connectionFilter.value)
  const hideRegex = quickFilterEnabled.value ? toSearchRegex(quickFilterRegex.value) : null
  const sourceIPs = sourceIPFilter.value
  const needSearchValues = Boolean(searchRegex || hideRegex)
  const displayOptions = {
    mode: isConnectionCard.value ? ('card' as const) : ('table' as const),
    proxyChainDirection: proxyChainDirection.value,
    showFullProxyChain: showFullProxyChain.value,
  }
  const visibleKeys = isConnectionCard.value
    ? connectionCardLines.value.flat()
    : connectionTableColumns.value

  return items.filter((conn) => {
    if (sourceIPs !== null && sourceIPs.every((i) => i !== getConnectionSourceIP(conn))) {
      return false
    }
    if (!needSearchValues) return true

    const visibleValues = getConnectionVisibleSearchValues(conn, visibleKeys, displayOptions)
    if (hideRegex?.testAny(visibleValues)) return false
    return searchRegex ? searchRegex.testAny(visibleValues) : true
  })
}

export const filteredActiveConnections = computed(() => filterConnections(activeConnections.value))

export const renderConnections = computed(() => {
  const filtered = filterConnections(connections.value)
  const sortType = isConnectionCard.value ? connectionSortType.value : SORT_TYPE.HOST
  const getSortKey = sortKeyFunctionMap[sortType]
  const descending = isConnectionCard.value && isDesc.value
  const decorated: [string | number, string, Connection][] = filtered.map((connection) => [
    getSortKey(connection),
    connection.id,
    connection,
  ])

  decorated.sort((left, right) => {
    const a = descending ? right : left
    const b = descending ? left : right
    const keyA = a[0]
    const keyB = b[0]
    const result =
      typeof keyA === 'number'
        ? keyA - (keyB as number)
        : keyA.localeCompare(keyB as string)

    return result || a[1].localeCompare(b[1])
  })

  return decorated.map((item) => item[2])
})
