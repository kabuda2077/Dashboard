import { CONNECTION_TAB_TYPE } from '@/constant'
import type { Connection } from '@/types'
import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

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

  it('replaces and releases its previous subscription when the runtime starts a new session', async () => {
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

    store.initConnections()
    expect(fetchConnectionsAPI).toHaveBeenCalledTimes(1)

    activeUuid.value = 'singbox'
    store.initConnections()

    expect(firstClose).toHaveBeenCalledTimes(1)
    expect(fetchConnectionsAPI).toHaveBeenCalledTimes(2)

    store.stopConnections()
    expect(secondClose).toHaveBeenCalledTimes(1)
  })
})
