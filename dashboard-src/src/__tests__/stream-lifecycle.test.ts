import { nextTick, ref, shallowRef, type ShallowRef } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LOG_LEVEL } from '@/constant'
import type { Connection, Log } from '@/types'
import type { ConnectionsSnapshot } from '@/assembly/connections'

describe('stream lifecycle', () => {
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
    vi.doUnmock('@/helper/backendSession')
    vi.doUnmock('@/assembly/version')
    vi.doUnmock('@/assembly/connections')
    vi.doUnmock('@/store/connections')
    vi.doUnmock('@/store/conn-history')
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
  it('retains paused and hidden closures, aggregates all entries, and discards old-session buffers', async () => {
    vi.resetModules()
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    const subscriptions: { data: ShallowRef<ConnectionsSnapshot | undefined>; close: ReturnType<typeof vi.fn> }[] = []
    const saveHistory = vi.fn()
    let generation = 1
    vi.doMock('@/assembly/version', () => ({ isSingBoxCore: ref(false) }))
    vi.doMock('@/helper/backendSession', () => ({
      backendSessionGeneration: ref(generation),
      backendSessionReady: ref(false),
      captureBackendSession: () => {
        const captured = generation
        return { isCurrent: () => generation === captured }
      },
    }))
    vi.doMock('@/assembly/connections', () => ({
      fetchConnectionsAPI: () => {
        const subscription = { data: shallowRef<ConnectionsSnapshot>(), close: vi.fn() }
        subscriptions.push(subscription)
        return subscription
      },
      disconnectByIdAPI: vi.fn(),
    }))
    vi.doMock('@/store/conn-history', () => ({
      initAggregatedDataMap: vi.fn(), saveConnectionHistory: saveHistory,
    }))
    const { initConnections, stopConnections, resetConnections, activeConnections, closedConnections, isPaused } =
      await import('@/store/connections')
    const conn = (id: string) => ({ id }) as Connection
    const send = async (closed: Connection[], active: Connection[] = [], index = subscriptions.length - 1) => {
      subscriptions[index]!.data.value = { active, closed } as ConnectionsSnapshot
      await nextTick()
    }
    isPaused.value = false
    initConnections()
    try {
      await send([], [conn('active')])
      isPaused.value = true
      const pausedClosures = Array.from({ length: 501 }, (_, index) => conn(`paused-${index}`))
      await send(pausedClosures)
      expect(closedConnections.value).toEqual([])
      expect(activeConnections.value.map(({ id }) => id)).toEqual(['active'])
      expect(saveHistory).toHaveBeenCalledExactlyOnceWith(pausedClosures)
      isPaused.value = false
      expect(activeConnections.value).toEqual([])
      expect(closedConnections.value).toHaveLength(500)
      expect(closedConnections.value[0]?.id).toBe('paused-1')
      visibility.mockReturnValue('hidden')
      await send([conn('hidden')])
      expect(closedConnections.value.at(-1)?.id).toBe('paused-500')
      visibility.mockReturnValue('visible')
      document.dispatchEvent(new Event('visibilitychange'))
      expect(closedConnections.value.at(-1)?.id).toBe('hidden')
      expect(saveHistory).toHaveBeenCalledTimes(2)
      visibility.mockReturnValue('hidden')
      await send([conn('old-buffer')])
      generation++
      initConnections()
      expect(subscriptions[0]!.close).toHaveBeenCalledOnce()
      await send([conn('late-old-message')], [], 0)
      await send([conn('new-buffer')])
      visibility.mockReturnValue('visible')
      // A visible snapshot also drains history if no visibility event was delivered.
      await send([conn('new-visible')], [conn('new-active')])
      expect(closedConnections.value.map(({ id }) => id)).toEqual(['new-buffer', 'new-visible'])
      expect(activeConnections.value.map(({ id }) => id)).toEqual(['new-active'])
      expect(saveHistory.mock.calls.flatMap(([batch]) => batch.map(({ id }: Connection) => id)))
        .not.toContain('late-old-message')
      isPaused.value = true
      await send([conn('stopped-buffer')])
      stopConnections()
      isPaused.value = false
      document.dispatchEvent(new Event('visibilitychange'))
      expect(closedConnections.value.map(({ id }) => id)).toEqual(['new-buffer', 'new-visible'])
      expect(subscriptions[1]!.close).toHaveBeenCalledOnce()
    } finally {
      resetConnections()
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
