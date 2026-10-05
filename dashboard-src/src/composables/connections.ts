import { backendSessionGeneration, captureBackendSession } from '@/helper/backendSession'
import type { Connection } from '@/types'
import { nextTick, ref, watch } from 'vue'

const infoConn = ref<Connection | null>(null)
const connectionDetailModalShow = ref(false)
watch(backendSessionGeneration, () => {
  connectionDetailModalShow.value = false
  infoConn.value = null
})

export const useConnections = () => {
  const handlerInfo = async (conn: Connection) => {
    const session = captureBackendSession()
    infoConn.value = null
    await nextTick()
    if (!session.isCurrent()) return
    infoConn.value = conn
    connectionDetailModalShow.value = true
  }

  return {
    infoConn,
    connectionDetailModalShow,
    handlerInfo,
  }
}
