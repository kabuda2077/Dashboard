import { CONNECTION_TAB_TYPE } from '@/constant'
import type { Connection } from '@/types'
import { describe, expect, it, vi } from 'vitest'
import { nextTick, ref } from 'vue'

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
    vi.doMock('@/store/conn-history', () => ({
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
    store.showClosedOnly.value = false
    await nextTick()
    store.connectionTabShow.value = CONNECTION_TAB_TYPE.ALL

    expect(store.connections.value.map(({ id }) => id)).toEqual(['closed', 'active'])
    store.showClosedOnly.value = true
    expect(store.isClosedOnlyView.value).toBe(true)
    expect(store.connections.value.map(({ id }) => id)).toEqual(['closed'])
    store.connectionTabShow.value = CONNECTION_TAB_TYPE.ACTIVE
    expect(store.isClosedOnlyView.value).toBe(false)
    expect(store.connections.value.map(({ id }) => id)).toEqual(['active'])
    store.showClosedOnly.value = false
    store.connectionTabShow.value = 'closedConnections' as CONNECTION_TAB_TYPE
    expect(store.connectionTabShow.value).toBe(CONNECTION_TAB_TYPE.ALL)
    expect(store.showClosedOnly.value).toBe(true)
    expect(store.connections.value.map(({ id }) => id)).toEqual(['closed'])
    await nextTick()
    const { getDashboardSettingsFromStorage, parseSettingsDocument, applyDashboardSettingsToStorage, clearDashboardSettingsFromStorage } = await import('@/helper/utils')
    const saved = getDashboardSettingsFromStorage()
    expect(saved['config/connection-show-closed-only']).toBe('true')
    expect((await import('@/store/settings')).showClosedOnly).toBe(store.showClosedOnly)
    clearDashboardSettingsFromStorage()
    expect(localStorage.getItem('config/connection-show-closed-only')).toBeNull()
    applyDashboardSettingsToStorage(parseSettingsDocument({ schemaVersion: 2, preferences: saved }))
    expect(localStorage.getItem('config/connection-show-closed-only')).toBe('true')
    const { useDashboardStorage } = await import('@/composables/use-storage')
    const restored = useDashboardStorage('config/connection-show-closed-only', false)
    expect(restored.value).toBe(true)
    store.showClosedOnly.value = false
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
    vi.doMock('@/store/conn-history', () => ({
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
