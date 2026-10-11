import { ref } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LOG_LEVEL } from '@/constant'
import type { Log } from '@/types'

describe('stream lifecycle', () => {
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
    vi.doUnmock('@/helper/backendSession')
    vi.doUnmock('@/assembly/version')
  })

  it('collects hidden and paused logs, flushes on resume, and detaches replaced sessions', async () => {
    vi.resetModules()
    vi.useFakeTimers()
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden')
    const removeListener = vi.spyOn(document, 'removeEventListener')
    const subscriptions: { push: (logs: Log[]) => void; close: ReturnType<typeof vi.fn> }[] = []
    let generation = 1
    vi.doMock('@/assembly/version', () => ({ isSingBoxCore: ref(false) }))
    vi.doMock('@/helper/backendSession', () => ({
      captureBackendSession: () => {
        const captured = generation
        return { isCurrent: () => generation === captured }
      },
    }))
    vi.doMock('@/assembly/logs/clash', () => ({
      subscribeLogs: vi.fn((_options, push) => {
        const subscription = { push, close: vi.fn() }
        subscriptions.push(subscription)
        return subscription
      }),
    }))
    const { initLogs, stopLogs, logs, isPaused } = await import('@/assembly/logs')
    const { logRetentionLimit } = await import('@/store/settings')
    logRetentionLimit.value = 2
    initLogs()
    const send = (payload: string, index = subscriptions.length - 1) =>
      subscriptions[index]!.push([{ type: LOG_LEVEL.Info, payload }])
    try {
      send('hidden')
      vi.advanceTimersByTime(1000)
      expect(logs.value).toEqual([])
      visibility.mockReturnValue('visible')
      document.dispatchEvent(new Event('visibilitychange'))
      expect(logs.value.map((log) => log.payload)).toEqual(['hidden'])
      isPaused.value = true
      send('paused')
      document.dispatchEvent(new Event('visibilitychange'))
      vi.advanceTimersByTime(1000)
      expect(logs.value.map((log) => log.payload)).toEqual(['hidden'])
      visibility.mockReturnValue('hidden')
      isPaused.value = false
      expect(logs.value.map((log) => log.payload)).toEqual(['hidden'])
      visibility.mockReturnValue('visible')
      document.dispatchEvent(new Event('visibilitychange'))
      expect(logs.value.map((log) => log.payload)).toEqual(['paused', 'hidden'])
      isPaused.value = true
      send('resume without message')
      isPaused.value = false
      expect(logs.value[0]?.payload).toBe('resume without message')
      generation++
      send('obsolete session')
      vi.advanceTimersByTime(500)
      expect(logs.value[0]?.payload).toBe('resume without message')
      initLogs() // The log-level control also replaces the active subscription.
      expect(subscriptions[0]!.close).toHaveBeenCalledOnce()
      send('late replaced callback', 0)
      send('replacement')
      send('pending at stop')
      stopLogs()
      send('late stopped callback')
      isPaused.value = true
      isPaused.value = false
      document.dispatchEvent(new Event('visibilitychange'))
      vi.advanceTimersByTime(500)
      expect(logs.value.map((log) => log.payload)).toEqual(['replacement'])
      expect(subscriptions[1]!.close).toHaveBeenCalledOnce()
      expect(removeListener.mock.calls.filter(([name]) => name === 'visibilitychange')).toHaveLength(2)
      expect(vi.getTimerCount()).toBe(0)
    } finally {
      stopLogs()
    }
  })
  it('stops the active log subscription', async () => {
    vi.resetModules()
    const close = vi.fn()

    vi.doMock('@/assembly/logs/clash', () => ({
      subscribeLogs: vi.fn(() => ({ close })),
    }))

    const { initLogs, stopLogs } = await import('@/assembly/logs')
    initLogs()
    stopLogs()
    stopLogs()

    expect(close).toHaveBeenCalledTimes(1)
  })

  it('stops memory and traffic statistic streams', async () => {
    vi.resetModules()
    const memoryClose = vi.fn()
    const trafficClose = vi.fn()

    vi.doMock('@/assembly/overview', () => ({
      fetchMemoryAPI: vi.fn(() => ({ data: ref(), close: memoryClose })),
      fetchTrafficAPI: vi.fn(() => ({ data: ref(), close: trafficClose })),
    }))
    vi.doMock('@/store/connections', () => ({
      activeConnectionCount: ref(0),
      downloadTotal: ref(0),
      uploadTotal: ref(0),
    }))

    const { initSatistic, stopSatistic } = await import('@/store/overview')
    initSatistic()
    stopSatistic()
    stopSatistic()

    expect(memoryClose).toHaveBeenCalledTimes(1)
    expect(trafficClose).toHaveBeenCalledTimes(1)
  })
})
