import { ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

describe('stream lifecycle', () => {
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
