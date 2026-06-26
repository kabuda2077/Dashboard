// sing-box native 后端的日志组装:订阅 gRPC SubscribeLog,保留 ANSI 颜色码、
// 映射日志级别,并把按批到达的日志逐条投递成与 Clash WS 相同的 { data, close } 流。
import { getSingboxClient } from '@/api/singbox/client'
import { runStream } from '@/api/singbox/streams'
import { LogLevel } from '@/gen/daemon/started_service_pb'
import type { Log } from '@/types'
import { ref, type Ref } from 'vue'

interface SingboxStream<T> {
  data: Ref<T | undefined>
  close: () => void
}

const logLevelToType = (level: LogLevel): Log['type'] => {
  switch (level) {
    case LogLevel.PANIC:
    case LogLevel.FATAL:
    case LogLevel.ERROR:
      return 'error'
    case LogLevel.WARN:
      return 'warning'
    case LogLevel.DEBUG:
    case LogLevel.TRACE:
      return 'debug'
    default:
      return 'info'
  }
}

const logLevelFilterFromParam = (level?: string): LogLevel | null | undefined => {
  switch (level?.toLowerCase()) {
    case 'panic':
      return LogLevel.PANIC
    case 'fatal':
      return LogLevel.FATAL
    case 'error':
      return LogLevel.ERROR
    case 'warning':
    case 'warn':
      return LogLevel.WARN
    case 'info':
      return LogLevel.INFO
    case 'debug':
      return LogLevel.DEBUG
    case 'trace':
      return LogLevel.TRACE
    case 'silent':
      return null
    default:
      return undefined
  }
}

const fetchSingboxLogs = <T>(params: Record<string, string> = {}): SingboxStream<T> => {
  const data = ref<T>()
  const client = getSingboxClient()?.client
  if (!client) return { data, close: () => {} }
  const levelFilter = logLevelFilterFromParam(params.level)

  // 日志按批到达,但消费方(logs store)逐条 watch ws.data,需要逐条投递。
  const queue: Log[] = []
  let draining = false
  const drain = () => {
    if (draining) return
    draining = true
    const step = () => {
      const next = queue.shift()
      if (!next) {
        draining = false
        return
      }
      data.value = next as T
      setTimeout(step, 0)
    }
    step()
  }

  const handle = runStream(
    (signal) => client.subscribeLog({}, { signal }),
    (msg) => {
      if (msg.reset) queue.length = 0
      for (const m of msg.messages) {
        if (levelFilter === null || (levelFilter !== undefined && m.level > levelFilter)) continue
        queue.push({ type: logLevelToType(m.level), payload: m.message })
      }
      drain()
    },
  )

  return {
    data,
    close: () => {
      queue.length = 0
      handle.close()
    },
  }
}

export const fetchLogsAPI = fetchSingboxLogs
