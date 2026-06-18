// Adapters that expose the sing-box native SubscribeStatus gRPC stream through
// the same `{ data, close }` shape that the Clash WebSocket factories return,
// so the statistics (memory / traffic) store can consume it without changes.

import { ref, type Ref } from 'vue'
import { getSingboxClient } from './client'
import { runStream } from './streams'

// SubscribeStatus reports at this interval (1s in ns), matching the cadence of
// the Clash traffic/memory WebSockets.
const SUBSCRIPTION_INTERVAL = 1_000_000_000n

export interface SingboxSubscription<T> {
  data: Ref<T | undefined>
  close: () => void
}

const subscribeSingboxStatus = <T>(
  map: (status: { memory: bigint; goroutines: number; uplink: bigint; downlink: bigint }) => T,
): SingboxSubscription<T> | null => {
  const client = getSingboxClient()?.client
  if (!client) return null

  const data = ref<T>()
  const handle = runStream(
    (signal) => client.subscribeStatus({ interval: SUBSCRIPTION_INTERVAL }, { signal }),
    (status) => {
      data.value = map(status)
    },
  )

  return { data, close: handle.close }
}

export const subscribeSingboxMemory = (): SingboxSubscription<{
  inuse: number
  goroutines: number
}> | null =>
  subscribeSingboxStatus((status) => ({
    inuse: Number(status.memory),
    goroutines: status.goroutines,
  }))

export const subscribeSingboxTraffic = (): SingboxSubscription<{
  down: number
  up: number
}> | null =>
  subscribeSingboxStatus((status) => ({
    down: Number(status.downlink),
    up: Number(status.uplink),
  }))
