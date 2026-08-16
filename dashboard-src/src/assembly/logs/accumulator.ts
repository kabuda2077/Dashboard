import { logRetentionLimit, sourceIPLabelList } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import type { Log, LogWithSeq } from '@/types'
import dayjs from 'dayjs'
import { throttle } from 'lodash'
import { watch, type Ref } from 'vue'

export interface LogsAccumulator {
  push: (batch: Log[]) => void
  dispose: () => void
}

export const createLogsAccumulator = (
  logs: Ref<LogWithSeq[]>,
  isPaused: () => boolean,
): LogsAccumulator => {
  let idx = 1
  let logsTemp: LogWithSeq[] = []

  const flush = throttle(() => {
    logs.value = logsTemp.concat(logs.value).slice(0, logRetentionLimit.value)
    logsTemp = []
  }, 500)

  const ipSourceMatches: [RegExp, string][] = []
  const restructureMatches = () => {
    ipSourceMatches.length = 0
    for (const { key, label, scope } of sourceIPLabelList.value) {
      if (scope && !scope.includes(activeBackend.value?.uuid as string)) continue
      if (key.startsWith('/')) continue

      if (key.includes(':')) {
        const regex = new RegExp(`${key}]:`, 'ig')
        ipSourceMatches.push([regex, `${key}] (${label}) :`])
      } else {
        const regex = new RegExp(`${key}:`, 'ig')
        ipSourceMatches.push([regex, `${key} (${label}) :`])
      }
    }
  }

  const stopWatch = watch(
    () => [sourceIPLabelList.value, activeBackend.value],
    () => restructureMatches(),
    { immediate: true, deep: true },
  )

  const push = (batch: Log[]) => {
    for (const data of batch) {
      if (isPaused()) {
        idx++
        continue
      }

      let payload = data.payload
      for (const [regex, label] of ipSourceMatches) {
        payload = payload.replace(regex, label)
      }

      logsTemp.unshift({
        ...data,
        payload,
        time: dayjs().format('HH:mm:ss'),
        seq: idx++,
      })
    }

    flush()
  }

  return {
    push,
    dispose: () => {
      stopWatch()
      flush.cancel()
    },
  }
}
