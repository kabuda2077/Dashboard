import { isSingboxBackend } from '@/assembly/backend'
import { LOG_LEVEL } from '@/constant'
import type { LogWithSeq } from '@/types'
import { useStorage } from '@vueuse/core'
import { ref } from 'vue'
import { createLogsAccumulator } from './accumulator'
import * as clash from './clash'
import * as singbox from './singbox'

export const logs = ref<LogWithSeq[]>([])
export const isPaused = ref(false)
export const logLevel = useStorage<string>('config/log-level', LOG_LEVEL.Info)

const backend = () => (isSingboxBackend.value ? singbox : clash)

let cancel: (() => void) | undefined

export const initLogs = () => {
  cancel?.()
  logs.value = []

  const accumulator = createLogsAccumulator(logs, () => isPaused.value)
  const subscription = backend().subscribeLogs({ level: logLevel.value }, accumulator.push)

  cancel = () => {
    accumulator.dispose()
    subscription.close()
  }
}
