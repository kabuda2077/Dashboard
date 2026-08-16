import { CONNECTION_TAB_TYPE } from '@/constant'
import type { Connection } from '@/types'
import { ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

describe('connection runtime', () => {
  it('merges active and closed connections and closes the stream explicitly', async () => {
    vi.resetModules()
    const close = vi.fn()
    const activeUuid = ref('mihomo')

    vi.doMock('@/assembly/connections', () => ({
      disconnectByIdAPI: vi.fn(),
      fetchConnectionsAPI: () => ({ data: ref(), close }),
      getConnectionVisibleSearchValues: vi.fn(() => []),
    }))
    vi.doMock('@/store/connHistory', () => ({
      initAggregatedDataMap: vi.fn(),
      saveConnectionHistory: vi.fn(),
    }))
    vi.doMock('@/store/setup', async (importOriginal) => ({
      ...(await importOriginal<typeof import('@/store/setup')>()),
      activeUuid,
    }))

    const store = await import('@/store/connections')
    const active = { id: 'active' } as Connection
    const closed = { id: 'closed' } as Connection

    store.activeConnections.value = [active]
    store.closedConnections.value = [closed]
    store.connectionTabShow.value = CONNECTION_TAB_TYPE.ALL

    expect(store.connections.value.map(({ id }) => id)).toEqual(['closed', 'active'])
    expect(store.isClosedConnection(closed)).toBe(true)
    expect(store.isClosedConnection(active)).toBe(false)

    store.initConnections()
    store.stopConnections()
    store.stopConnections()
    expect(close).toHaveBeenCalledTimes(1)
  })

  it('restarts the connection stream when the backend changes', async () => {
    vi.resetModules()
    const activeUuid = ref('mihomo')
    const firstClose = vi.fn()
    const secondClose = vi.fn()
    const fetchConnectionsAPI = vi
      .fn()
      .mockReturnValueOnce({ data: ref(), close: firstClose })
      .mockReturnValueOnce({ data: ref(), close: secondClose })

    vi.doMock('@/assembly/connections', () => ({
      disconnectByIdAPI: vi.fn(),
      fetchConnectionsAPI,
      getConnectionVisibleSearchValues: vi.fn(() => []),
    }))
    vi.doMock('@/store/connHistory', () => ({
      initAggregatedDataMap: vi.fn(),
      saveConnectionHistory: vi.fn(),
    }))
    vi.doMock('@/store/setup', async (importOriginal) => ({
      ...(await importOriginal<typeof import('@/store/setup')>()),
      activeUuid,
    }))

    const store = await import('@/store/connections')

    store.initConnections('full')
    store.initConnections('full')
    expect(fetchConnectionsAPI).toHaveBeenCalledTimes(1)

    activeUuid.value = 'singbox'
    store.initConnections('full')

    expect(firstClose).toHaveBeenCalledTimes(1)
    expect(fetchConnectionsAPI).toHaveBeenCalledTimes(2)

    store.stopConnections()
    expect(secondClose).toHaveBeenCalledTimes(1)
  })
})
