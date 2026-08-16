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
import { useStorage, watchOnce } from '@vueuse/core'
import dayjs from 'dayjs'
import { computed, ref, watch } from 'vue'
import { initAggregatedDataMap, saveConnectionHistory } from './connHistory'
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

export const activeConnections = ref<Connection[]>([])
export const closedConnections = ref<Connection[]>([])
export const activeConnectionCount = ref(0)
export const isPaused = ref(false)

export const downloadTotal = ref(0)
export const uploadTotal = ref(0)

let cancel: (() => void) | undefined
type ConnectionsMode = 'summary' | 'full'
let connectionMode: ConnectionsMode | null = null

export const initConnections = (mode: ConnectionsMode = 'full') => {
  if (cancel && connectionMode === mode) {
    return
  }

  cancel?.()
  connectionMode = mode
  activeConnections.value = []
  closedConnections.value = []
  activeConnectionCount.value = 0
  downloadTotal.value = 0
  uploadTotal.value = 0
  initAggregatedDataMap()
  const ws = fetchConnectionsAPI()
  const unwatch = watch(ws.data, (snapshot) => {
    if (!snapshot) return

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

  if (autoDisconnectIdleUDP.value) {
    watchOnce(activeConnections, () => {
      activeConnections.value
        .filter((conn) => getConnectionNetwork(conn) !== 'tcp')
        .forEach((conn) => {
          const now = dayjs()
          const start = dayjs(getConnectionStart(conn))

          if (now.diff(start, 'minute') > autoDisconnectIdleUDPTime.value) {
            disconnectByIdAPI(conn.id)
          }
        })
    })
  }

  cancel = () => {
    unwatch()
    ws.close()
  }
}

const isDesc = computed(() => {
  return connectionSortDirection.value === SORT_DIRECTION.DESC
})

const sortFunctionMap: Record<SORT_TYPE, (a: Connection, b: Connection) => number> = {
  [SORT_TYPE.HOST]: (a: Connection, b: Connection) => {
    return getHostFromConnection(a).localeCompare(getHostFromConnection(b))
  },
  [SORT_TYPE.RULE]: (a: Connection, b: Connection) => {
    return getConnectionRule(a).localeCompare(getConnectionRule(b))
  },
  [SORT_TYPE.CHAINS]: (a: Connection, b: Connection) => {
    return getChainsStringFromConnection(a).localeCompare(getChainsStringFromConnection(b))
  },
  [SORT_TYPE.DOWNLOAD]: (a: Connection, b: Connection) => {
    return getConnectionDownload(a) - getConnectionDownload(b)
  },
  [SORT_TYPE.DOWNLOAD_SPEED]: (a: Connection, b: Connection) => {
    return a.downloadSpeed - b.downloadSpeed
  },
  [SORT_TYPE.UPLOAD]: (a: Connection, b: Connection) => {
    return getConnectionUpload(a) - getConnectionUpload(b)
  },
  [SORT_TYPE.UPLOAD_SPEED]: (a: Connection, b: Connection) => {
    return a.uploadSpeed - b.uploadSpeed
  },
  [SORT_TYPE.SOURCE_IP]: (a: Connection, b: Connection) => {
    return getConnectionSourceIP(a).localeCompare(getConnectionSourceIP(b))
  },
  [SORT_TYPE.TYPE]: (a: Connection, b: Connection) => {
    return getNetworkTypeFromConnection(a).localeCompare(getNetworkTypeFromConnection(b))
  },
  [SORT_TYPE.CONNECT_TIME]: (a: Connection, b: Connection) => {
    return dayjs(getConnectionStart(a)).valueOf() - dayjs(getConnectionStart(b)).valueOf()
  },
  [SORT_TYPE.INBOUND_USER]: (a: Connection, b: Connection) => {
    return getInboundUserFromConnection(a).localeCompare(getInboundUserFromConnection(b))
  },
}

export const connections = computed(() => {
  return connectionTabShow.value === CONNECTION_TAB_TYPE.ACTIVE
    ? activeConnections.value
    : closedConnections.value
})

const filterConnections = (items: readonly Connection[]) => {
  const searchRegex = toSearchRegex(connectionFilter.value)
  const hideRegex = quickFilterEnabled.value ? toSearchRegex(quickFilterRegex.value) : null
  const displayOptions = {
    mode: isConnectionCard.value ? ('card' as const) : ('table' as const),
    proxyChainDirection: proxyChainDirection.value,
    showFullProxyChain: showFullProxyChain.value,
  }
  const visibleKeys = isConnectionCard.value
    ? connectionCardLines.value.flat()
    : connectionTableColumns.value

  return items
    .filter((conn) => {
      const visibleValues = getConnectionVisibleSearchValues(conn, visibleKeys, displayOptions)

      if (
        sourceIPFilter.value !== null &&
        sourceIPFilter.value.every((i) => i !== getConnectionSourceIP(conn))
      ) {
        return false
      }

      if (hideRegex) {
        const quickFilterMatch = hideRegex.testAny(visibleValues)

        if (quickFilterMatch) {
          return false
        }
      }

      if (searchRegex) {
        return searchRegex.testAny(visibleValues)
      }

      return true
    })
}

export const filteredActiveConnections = computed(() => filterConnections(activeConnections.value))

export const renderConnections = computed(() => {
  return filterConnections(connections.value)
    .sort((a, b) => {
      if (isConnectionCard.value && isDesc.value) {
        ;[a, b] = [b, a]
      }
      const sortResult = isConnectionCard.value
        ? sortFunctionMap[connectionSortType.value](a, b)
        : sortFunctionMap[SORT_TYPE.HOST](a, b)

      if (sortResult === 0) {
        return a.id.localeCompare(b.id)
      }

      return sortResult
    })
})
