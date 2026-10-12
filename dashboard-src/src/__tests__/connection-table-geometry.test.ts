import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App, type ComputedRef, type Ref } from 'vue'
import type { Virtualizer, VirtualizerOptions } from '@tanstack/vue-virtual'
import type { Connection } from '@/types'

const state = vi.hoisted(() => ({
  preferences: new Map<string, Ref<unknown>>(),
  offsetChanged: undefined as ((offset: number, scrolling: boolean) => void) | undefined,
}))
let virtualizer: Ref<Virtualizer<HTMLElement, Element>>
vi.mock('@/assembly/connections', () => ({
  disconnectByIdAPI: vi.fn(), blockConnectionByIdAPI: vi.fn(),
  getConnectionDisplayValue: (conn: Connection, key: string) => key === 'host' ? conn.id : '',
}))
vi.mock('@/composables/use-connections', () => ({ useConnections: () => ({ handlerInfo: vi.fn() }) }))
vi.mock('@/helper', () => ({
  getConnectionChains: () => [], getConnectionDownload: () => 0, getConnectionUpload: () => 0,
  getConnectionStart: (conn: Connection) => conn.start, getConnectionSmartBlock: () => false,
}))
vi.mock('@/helper/indexeddb', async () => ({ backgroundImage: (await import('vue')).ref(null) }))
vi.mock('@/helper/clipboard', () => ({ copyToClipboard: vi.fn() }))
vi.mock('@/helper/request-error', () => ({ runManualRequest: vi.fn() }))
vi.mock('@/composables/use-storage', async () => {
  const { ref } = await import('vue')
  return { useDashboardStorage: (key: string, defaults: unknown) => {
    if (!state.preferences.has(key)) state.preferences.set(key, ref(defaults))
    return state.preferences.get(key)
  } }
})
vi.mock('@/store/connections', async () => {
  const { ref, computed } = await import('vue')
  const connectionTabShow = ref('allConnections')
  const showClosedOnly = ref(false)
  return { connectionFilter: ref(''), connectionTabShow, showClosedOnly,
    isClosedOnlyView: computed(() => connectionTabShow.value === 'allConnections' && showClosedOnly.value),
    renderConnections: ref<Connection[]>([]),
    isClosedConnection: (conn: Connection) => conn.id.startsWith('closed') }
})
vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  const { TABLE_SIZE, TABLE_WIDTH_MODE, CONNECTIONS_TABLE_ACCESSOR_KEY, PROXY_CHAIN_DIRECTION } = await import('@/constant')
  return { connectionTableColumns: ref([CONNECTIONS_TABLE_ACCESSOR_KEY.Close, CONNECTIONS_TABLE_ACCESSOR_KEY.Host]),
    proxyChainDirection: ref(PROXY_CHAIN_DIRECTION.NORMAL), showFullProxyChain: ref(false),
    tableSize: ref(TABLE_SIZE.LARGE), tableWidthMode: ref(TABLE_WIDTH_MODE.AUTO) }
})
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/components/common/HighlightText.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ props: ['text'], setup: (props) => () => h('span', props.text) }) }
})
vi.mock('@/components/proxies/ProxyName.vue', () => ({ default: { render: () => null } }))
// Exercise the real virtualizer and Vue table; substitute only layout observations unavailable in Happy DOM.
vi.mock('@tanstack/vue-virtual', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tanstack/vue-virtual')>()
  const { computed } = await import('vue')
  return { ...actual, useVirtualizer: (options: ComputedRef<VirtualizerOptions<HTMLElement, Element>>) => {
    virtualizer = actual.useVirtualizer(computed(() => ({
      ...options.value,
      observeElementRect: (_: unknown, callback: (rect: { width: number; height: number }) => void) => {
        callback({ width: 600, height: 120 })
        return () => {}
      },
      observeElementOffset: (_: unknown, callback: (offset: number, scrolling: boolean) => void) => {
        state.offsetChanged = callback
        callback(0, false)
        return () => { state.offsetChanged = undefined }
      },
      scrollToFn: () => {},
    })))
    return virtualizer
  } }
})

import ConnectionTable from '@/components/connections/ConnectionTable.vue'
import { CONNECTION_TAB_TYPE, CONNECTIONS_TABLE_ACCESSOR_KEY as KEY, TABLE_SIZE } from '@/constant'
import { connectionTabShow, renderConnections, showClosedOnly } from '@/store/connections'
import { tableSize } from '@/store/settings'

let app: App | undefined
let root: HTMLDivElement
const conn = (id: string) => ({ id, start: '2026-10-01T00:00:00Z' }) as Connection
const dataRows = () => [...root.querySelectorAll<HTMLTableRowElement>('tbody tr[style]')]
  .filter((row) => row.querySelector('td'))
const mount = async (connections: Connection[]) => {
  renderConnections.value = connections
  app = createApp(ConnectionTable)
  app.mount(root)
  await nextTick()
  await nextTick()
}
beforeEach(() => {
  state.preferences.clear()
  state.offsetChanged = undefined
  tableSize.value = TABLE_SIZE.LARGE
  connectionTabShow.value = CONNECTION_TAB_TYPE.ALL
  showClosedOnly.value = false
  root = document.createElement('div')
  document.body.append(root)
})
afterEach(() => { app?.unmount(); app = undefined; root.remove() })

describe('connection table virtual row geometry', () => {
  it.each([[TABLE_SIZE.LARGE, 40], [TABLE_SIZE.SMALL, 36]] as const)(
    'renders active and closed rows at the same %s height', async (size, height) => {
      tableSize.value = size
      await mount([conn('active'), conn('closed')])
      expect(virtualizer.value.getTotalSize()).toBe(height * 2)
      expect(dataRows().map((row) => row.style.height)).toEqual([`${height}px`, `${height}px`])
      expect(dataRows()[0].querySelector('button')).not.toBeNull()
      expect(dataRows()[1].querySelector('button')).toBeNull()
      expect(dataRows()[1].classList.contains('opacity-60')).toBe(true)
    },
  )

  it('keeps actual virtual positions consistent through a long mixed list', async () => {
    await mount(Array.from({ length: 100 }, (_, i) => conn(i % 2 ? `closed-${i}` : `active-${i}`)))
    expect(dataRows().length).toBeLessThan(100)
    expect(virtualizer.value.getTotalSize()).toBe(4000)
    state.offsetChanged!(3960, false)
    await nextTick()
    const last = virtualizer.value.getVirtualItems().find((row) => row.index === 99)!
    expect(last.start).toBe(3960)
    expect(last.size).toBe(40)
    expect(last.key).toBe('closed-99')
    expect(dataRows().at(-1)!.textContent).toContain('closed-99')
  })

  it('updates geometry when switching sizes without losing stable connection keys', async () => {
    await mount([conn('active'), conn('closed')])
    tableSize.value = TABLE_SIZE.SMALL
    await nextTick()
    expect(virtualizer.value.getTotalSize()).toBe(72)
    expect(virtualizer.value.getVirtualItems().map((row) => row.key)).toEqual(['active', 'closed'])
    tableSize.value = TABLE_SIZE.LARGE
    await nextTick()
    expect(virtualizer.value.getTotalSize()).toBe(80)
  })

  it('keeps the existing sort and grouping row models instead of using virtual indices as keys', async () => {
    await mount([conn('b'), conn('a')])
    root.querySelectorAll<HTMLTableCellElement>('th')[1].click()
    await nextTick()
    expect(virtualizer.value.getVirtualItems().map((row) => row.key)).toEqual(['a', 'b'])
    state.preferences.get('config/table-grouping')!.value = [KEY.Host]
    await nextTick()
    expect(virtualizer.value.options.count).toBe(2)
    expect(virtualizer.value.getVirtualItems().map((row) => row.key)).toEqual(['host:a', 'host:b'])
    expect(dataRows().map((row) => row.style.height)).toEqual(['40px', '40px'])
  })

  it('hides the Close column and keeps closed-only rows undimmed', async () => {
    showClosedOnly.value = true
    await mount([conn('closed')])
    expect(root.querySelectorAll('th')).toHaveLength(1)
    expect(dataRows()[0].classList.contains('opacity-60')).toBe(false)
    expect(dataRows()[0].style.height).toBe('40px')
  })
})
