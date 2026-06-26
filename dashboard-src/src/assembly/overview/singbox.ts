// sing-box native 后端的概览统计组装:多个订阅者共享一条 gRPC SubscribeStatus 流,
// 把 Status 映射成 memory / traffic,并以与 Clash WS 相同的 { data, close } 形状产出。
import { getSingboxClient } from '@/api/singbox/client'
import { runStream, type StreamHandle } from '@/api/singbox/streams'
import type { Status } from '@/gen/daemon/started_service_pb'
import { ref, watch, type Ref } from 'vue'

// SubscribeStatus 的上报间隔(1s in ns),与 Clash traffic/memory WebSocket 的节奏一致。
const SUBSCRIPTION_INTERVAL = 1_000_000_000n

interface SingboxStream<T> {
  data: Ref<T | undefined>
  close: () => void
}

type StatusListener = (status: Status) => void

const statusListeners = new Set<StatusListener>()
let statusHandle: StreamHandle | null = null
let latestStatus: Status | null = null

const closeSharedStatusStream = () => {
  statusHandle?.close()
  statusHandle = null
  latestStatus = null
}

const ensureSharedStatusStream = () => {
  if (statusHandle) return true

  const client = getSingboxClient()?.client
  if (!client) return false

  statusHandle = runStream(
    (signal) => client.subscribeStatus({ interval: SUBSCRIPTION_INTERVAL }, { signal }),
    (status) => {
      latestStatus = status
      statusListeners.forEach((listener) => listener(status))
    },
  )

  return true
}

const subscribeSingboxStatus = <T>(map: (status: Status) => T): SingboxStream<T> | null => {
  const data = ref<T>()
  const listener: StatusListener = (status) => {
    data.value = map(status)
  }

  statusListeners.add(listener)
  if (!ensureSharedStatusStream()) {
    statusListeners.delete(listener)
    return null
  }
  if (latestStatus) listener(latestStatus)

  return {
    data,
    close: () => {
      statusListeners.delete(listener)
      if (statusListeners.size === 0) closeSharedStatusStream()
    },
  }
}

const createSingboxStat = <T>(kind: 'memory' | 'traffic'): SingboxStream<T> => {
  const data = ref<T>()
  const sub =
    kind === 'memory'
      ? subscribeSingboxStatus((status) => ({
          inuse: Number(status.memory),
          goroutines: status.goroutines,
        }))
      : subscribeSingboxStatus((status) => ({
          down: Number(status.downlink),
          up: Number(status.uplink),
        }))

  if (!sub) return { data, close: () => {} }
  watch(sub.data, (value) => (data.value = value as T), { immediate: true })
  return { data, close: sub.close }
}

export const fetchMemoryAPI = <T>() => createSingboxStat<T>('memory')

export const fetchTrafficAPI = <T>() => createSingboxStat<T>('traffic')
