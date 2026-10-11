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

  it('keeps display frozen while paused and resumes with only the latest retained logs', async () => {
    const { createLogsAccumulator } = await import('@/assembly/logs/accumulator')
    const { logRetentionLimit } = await import('@/store/settings')
    logRetentionLimit.value = 3
    const logs = ref<LogWithSeq[]>([])
    let paused = false
    const accumulator = createLogsAccumulator(logs, () => paused)
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'visible' }])
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'queued before pause' }])
    paused = true
    accumulator.push(
      Array.from({ length: 10000 }, (_, i) => ({
        type: LOG_LEVEL.Info,
        payload: `paused ${i}`,
      })),
    )
    vi.advanceTimersByTime(1000)
    expect(logs.value.map((log) => log.payload)).toEqual(['visible'])
    expect(vi.getTimerCount()).toBe(0)
    logRetentionLimit.value = 2
    paused = false
    accumulator.flush()
    expect(logs.value.map((log) => [log.payload, log.seq])).toEqual([
      ['paused 9999', 10002],
      ['paused 9998', 10001],
    ])
    logRetentionLimit.value = 0
    accumulator.flush()
    expect(logs.value).toEqual([])
    accumulator.dispose()
  })

  it('clear removes pending logs immediately and disposal cancels future publication', async () => {
    const { createLogsAccumulator } = await import('@/assembly/logs/accumulator')
    const logs = ref<LogWithSeq[]>([])
    let paused = false
    const accumulator = createLogsAccumulator(logs, () => paused)
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'visible' }])
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'pending' }])
    logs.value = []
    vi.advanceTimersByTime(500)
    expect(logs.value).toEqual([])
    paused = true
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'paused' }])
    logs.value = [] // Clear also works when the displayed array is already empty.
    paused = false
    accumulator.flush()
    expect(logs.value).toEqual([])
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'after clear' }])
    expect(logs.value[0]).toMatchObject({ payload: 'after clear', seq: 4 })
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'pending on disposal' }])
    accumulator.dispose()
    accumulator.push([{ type: LOG_LEVEL.Info, payload: 'late callback' }])
    accumulator.flush()
    vi.advanceTimersByTime(500)
    expect(logs.value.map((log) => log.payload)).toEqual(['after clear'])
    expect(vi.getTimerCount()).toBe(0)
  })
})
