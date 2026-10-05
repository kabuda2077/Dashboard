import { isSingBoxCore } from '@/assembly/version'
import { LOG_LEVEL } from '@/constant'
import { captureBackendSession } from '@/helper/backendSession'
import { useStorage } from '@/helper/storage'
import type { LogWithSeq } from '@/types'
import { computed, ref, shallowRef } from 'vue'
import { createLogsAccumulator } from './accumulator'
import * as clash from './clash'

export const logs = shallowRef<LogWithSeq[]>([])
export const isPaused = ref(false)
export const logLevel = useStorage<string>('config/log-level', LOG_LEVEL.Info)
export const supportedLogLevels = computed(() =>
  isSingBoxCore.value
    ? Object.values(LOG_LEVEL)
    : [LOG_LEVEL.Debug, LOG_LEVEL.Info, LOG_LEVEL.Warning, LOG_LEVEL.Error, LOG_LEVEL.Silent],
)

let cancel: (() => void) | undefined

export const initLogs = () => {
  cancel?.()
  if (!supportedLogLevels.value.includes(logLevel.value as LOG_LEVEL))
    logLevel.value = LOG_LEVEL.Info
  logs.value = []
  const accumulator = createLogsAccumulator(
    logs,
    () => isPaused.value || document.visibilityState === 'hidden',
  )
  const session = captureBackendSession()
  const subscription = clash.subscribeLogs({ level: logLevel.value }, (log) => {
    if (session.isCurrent()) accumulator.push(log)
  })

  cancel = () => {
    accumulator.dispose()
    subscription.close()
  }
}

export const stopLogs = () => {
  cancel?.()
  cancel = undefined
}

export const resetLogs = () => {
  stopLogs()
  logs.value = []
}
