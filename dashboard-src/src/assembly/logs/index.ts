import { LOG_LEVEL } from '@/constant'
import { useStorage } from '@/helper/storage'
import type { LogWithSeq } from '@/types'
import { ref, shallowRef } from 'vue'
import { createLogsAccumulator } from './accumulator'
import * as clash from './clash'

export const logs = shallowRef<LogWithSeq[]>([])
export const isPaused = ref(false)
export const logLevel = useStorage<string>('config/log-level', LOG_LEVEL.Info)

let cancel: (() => void) | undefined

export const initLogs = () => {
  cancel?.()
  logs.value = []
  const accumulator = createLogsAccumulator(logs, () => isPaused.value)
  const subscription = clash.subscribeLogs({ level: logLevel.value }, accumulator.push)

  cancel = () => {
    accumulator.dispose()
    subscription.close()
  }
}

export const stopLogs = () => {
  cancel?.()
  cancel = undefined
}
