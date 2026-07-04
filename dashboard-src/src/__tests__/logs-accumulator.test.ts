import { LOG_LEVEL } from '@/constant'
import type { LogWithSeq } from '@/types'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

describe('logs accumulator', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-01-02T03:04:05'))
    localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('batches logs and applies source-ip labels', async () => {
    const { backendList, activeUuid } = await import('@/store/setup')
    const { logRetentionLimit, sourceIPLabelList } = await import('@/store/settings')
    const { createLogsAccumulator } = await import('@/assembly/logs/accumulator')

    backendList.value = [
      {
        type: 'clash',
        protocol: 'http',
        host: '127.0.0.1',
        port: '9090',
        secondaryPath: '',
        password: '',
        uuid: 'backend-a',
      },
    ]
    activeUuid.value = 'backend-a'
    sourceIPLabelList.value = [
      { id: 'label-a', key: '10.0.0.2', label: 'Phone', scope: ['backend-a'] },
    ]
    logRetentionLimit.value = 3

    const logs = ref<LogWithSeq[]>([])
    const accumulator = createLogsAccumulator(logs, () => false)

    accumulator.push([
      { type: LOG_LEVEL.Info, payload: '10.0.0.2: matched' },
      { type: LOG_LEVEL.Warning, payload: 'keep me' },
      { type: LOG_LEVEL.Error, payload: 'trim me' },
    ])

    vi.advanceTimersByTime(500)

    expect(logs.value).toHaveLength(3)
    expect(logs.value[0]).toMatchObject({ type: LOG_LEVEL.Error, payload: 'trim me', seq: 3 })
    expect(logs.value[1]).toMatchObject({
      type: LOG_LEVEL.Warning,
      payload: 'keep me',
      seq: 2,
    })
    expect(logs.value[2]).toMatchObject({
      type: LOG_LEVEL.Info,
      payload: '10.0.0.2 (Phone) : matched',
      seq: 1,
    })

    accumulator.dispose()
  })

  it('drops paused logs while preserving sequence order', async () => {
    const { createLogsAccumulator } = await import('@/assembly/logs/accumulator')

    const logs = ref<LogWithSeq[]>([])
    let paused = true
    const accumulator = createLogsAccumulator(logs, () => paused)

    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'paused' }])
    paused = false
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'visible' }])

    vi.advanceTimersByTime(500)

    expect(logs.value).toHaveLength(1)
    expect(logs.value[0]).toMatchObject({ payload: 'visible', seq: 2 })

    accumulator.dispose()
  })
})
