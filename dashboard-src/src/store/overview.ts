import { fetchMemoryAPI, fetchTrafficAPI } from '@/assembly/overview'
import { ref, watch } from 'vue'
import { activeConnectionCount, downloadTotal, uploadTotal } from './connections'

export interface HistoryPoint {
  name: number
  value: [number, number]
  init?: boolean
}

export const timeSaved = 60
const bufferPoints = 2
const savedPoints = timeSaved + bufferPoints

const makeInitValue = (): HistoryPoint[] => {
  const now = Date.now()
  return new Array(savedPoints).fill(0).map((_, index) => {
    const timestamp = now - (savedPoints - 1 - index) * 1000
    return { name: timestamp, value: [timestamp, 0], init: true }
  })
}

export const memory = ref<number>(0)
export const memoryHistory = ref(makeInitValue())
export const connectionsHistory = ref(makeInitValue())

export const downloadSpeed = ref<number>(0)
export const uploadSpeed = ref<number>(0)
export const downloadSpeedHistory = ref(makeInitValue())
export const uploadSpeedHistory = ref(makeInitValue())

let cancel: () => void

export const initSatistic = () => {
  cancel?.()

  downloadSpeedHistory.value = makeInitValue()
  uploadSpeedHistory.value = makeInitValue()
  memoryHistory.value = makeInitValue()
  connectionsHistory.value = makeInitValue()

  const { data: memoryWsData, close: memoryWsClose } = fetchMemoryAPI<{
    inuse: number
  }>()
  const unwatchMemory = watch(
    () => memoryWsData.value,
    (data) => {
      if (!data) return
      const timestamp = Date.now().valueOf()

      if (data.inuse === 0) {
        return
      }

      memory.value = data.inuse
      memoryHistory.value.push({
        value: [timestamp, data.inuse],
        name: timestamp,
      })
      connectionsHistory.value.push({
        value: [timestamp, activeConnectionCount.value],
        name: timestamp,
      })

      memoryHistory.value = memoryHistory.value.slice(-savedPoints)
      connectionsHistory.value = connectionsHistory.value.slice(-savedPoints)
    },
  )

  const { data: trafficWsData, close: trafficWsClose } = fetchTrafficAPI<{
    down: number
    up: number
    downTotal?: number
    upTotal?: number
  }>()
  const unwatchTraffic = watch(
    () => trafficWsData.value,
    (data) => {
      if (!data) return

      const timestamp = Date.now().valueOf()

      downloadSpeed.value = data.down
      uploadSpeed.value = data.up
      if (data.downTotal != null && data.upTotal != null) {
        downloadTotal.value = data.downTotal
        uploadTotal.value = data.upTotal
      }

      downloadSpeedHistory.value.push({
        value: [timestamp, data.down],
        name: timestamp,
      })
      uploadSpeedHistory.value.push({
        value: [timestamp, data.up],
        name: timestamp,
      })

      downloadSpeedHistory.value = downloadSpeedHistory.value.slice(-savedPoints)
      uploadSpeedHistory.value = uploadSpeedHistory.value.slice(-savedPoints)
    },
  )

  cancel = () => {
    memoryWsClose()
    trafficWsClose()
    unwatchMemory()
    unwatchTraffic()
  }
}
