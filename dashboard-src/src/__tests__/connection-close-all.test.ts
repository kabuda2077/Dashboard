import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'
import type { Connection } from '@/types'

const calls = vi.hoisted(() => ({ all: vi.fn(), byId: vi.fn(), notify: vi.fn(), generation: 1 }))
vi.mock('@/assembly/connections', () => ({ disconnectAllAPI: calls.all, disconnectByIdAPI: calls.byId }))
vi.mock('@/helper/backendSession', () => ({ captureBackendSession: () => {
  const generation = calls.generation
  return { isCurrent: () => generation === calls.generation }
} }))
vi.mock('@/helper/notification', () => ({ showNotification: calls.notify }))
vi.mock('@/store/connections', async () => {
  const { ref, computed } = await import('vue')
  const { CONNECTION_TAB_TYPE } = await import('@/constant')
  const activeConnections = ref<Connection[]>([])
  const closedConnections = ref<Connection[]>([])
  const connectionTabShow = ref(CONNECTION_TAB_TYPE.ACTIVE)
  const showClosedOnly = ref(false)
  const isClosedOnlyView = computed(() => connectionTabShow.value === CONNECTION_TAB_TYPE.ALL && showClosedOnly.value)
  return {
    activeConnections, closedConnections, connectionTabShow, showClosedOnly, isClosedOnlyView,
    connections: computed(() => isClosedOnlyView.value ? closedConnections.value : connectionTabShow.value === CONNECTION_TAB_TYPE.ALL
      ? [...closedConnections.value, ...activeConnections.value] : activeConnections.value),
    renderConnections: ref<Connection[]>([]), isPaused: ref(false),
    isClosedConnection: (conn: Connection) => closedConnections.value.some((closed) => closed.id === conn.id),
    connectionFilter: ref(''), connectionSortDirection: ref('asc'), connectionSortType: ref('host'),
    quickFilterEnabled: ref(false), quickFilterRegex: ref(''), searchHiddenColumns: ref(false),
  }
})
vi.mock('@/store/settings', async () => ({ isConnectionCard: (await import('vue')).ref(false) }))
vi.mock('@/helper/connection-card-groups', async () => {
  const { ref } = await import('vue')
  return { CONNECTION_CARD_GROUPABLE_KEYS: [], connectionCardGroupKey: ref(null),
    hasConnectionCardGroups: ref(false), hasExpandedConnectionCardGroups: ref(false), toggleAllConnectionCardGroups: vi.fn() }
})
vi.mock('@/composables/use-ctrls-bar', async () => {
  const { ref } = await import('vue')
  return { useCtrlsBar: () => ({ isLargeCtrlsBar: ref(false) }) }
})
vi.mock('@/helper/tooltip', () => ({ useTooltip: () => ({ showTip: vi.fn(), updateTip: vi.fn() }) }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('@/components/common/CtrlsBar.vue', async () => {
  const { defineComponent, h } = await import('vue')
  return { default: defineComponent({ setup: (_, { slots }) => () => h('div', slots.default?.()) }) }
})
vi.mock('@/components/common/DialogWrapper.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/connections/ConnectionCardSettings.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/settings/connections/TableSettings.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/controls/SourceIPFilter.vue', () => ({ default: { render: () => null } }))

import ConnectionCtrl from '@/components/controls/ConnectionCtrl'
import { CONNECTION_TAB_TYPE } from '@/constant'
import { activeConnections, closedConnections, connectionTabShow, isPaused, renderConnections, showClosedOnly } from '@/store/connections'

let app: App | undefined
let root: HTMLDivElement
const conn = (id: string, start = '2026-10-01T00:00:00Z') => ({ id, start }) as Connection
const closeButton = () => root.querySelector<SVGPathElement>(
  'button.btn-circle.btn-sm path[d="M6 18 18 6M6 6l12 12"]',
)!.closest<HTMLButtonElement>('button')!
const clickClose = async () => {
  // Dispatch also exercises the handler guard, even if native clicks would be blocked by disabled.
  closeButton().dispatchEvent(new MouseEvent('click', { bubbles: true }))
  await nextTick()
  await Promise.resolve()
}
const mount = async (active: Connection[], closed: Connection[] = [], tab = CONNECTION_TAB_TYPE.ACTIVE) => {
  activeConnections.value = active
  closedConnections.value = closed
  connectionTabShow.value = tab
  renderConnections.value = showClosedOnly.value && tab === CONNECTION_TAB_TYPE.ALL ? closedConnections.value
    : tab === CONNECTION_TAB_TYPE.ALL ? [...closedConnections.value, ...activeConnections.value]
      : activeConnections.value
  app = createApp(ConnectionCtrl)
  app.mount(root)
  await nextTick()
}
beforeEach(() => {
  vi.clearAllMocks()
  calls.generation = 1
  calls.all.mockResolvedValue(undefined)
  calls.byId.mockResolvedValue(undefined)
  isPaused.value = false
  showClosedOnly.value = false
  root = document.createElement('div')
  root.id = 'app-content'
  document.body.append(root)
})
afterEach(() => { app?.unmount(); app = undefined; root.remove() })

describe('close visible current active connections', () => {
  it('opens the All filter only on reselect and restores keyboard focus', async () => {
    await mount([conn('active')], [conn('closed')])
    const tabs = [...root.querySelectorAll<HTMLButtonElement>('[role=tab]')]
    expect(tabs).toHaveLength(2)
    const all = tabs[1]!
    expect(all.getAttribute('aria-haspopup')).toBe('menu')
    expect(all.getAttribute('aria-expanded')).toBe('false')
    all.click()
    await nextTick()
    expect(root.querySelector('[role=menu]')).toBeNull()
    all.click()
    await nextTick(); await nextTick()
    const items = [...root.querySelectorAll<HTMLButtonElement>('[role=menuitemradio]')]
    expect(items).toHaveLength(2)
    expect(all.getAttribute('aria-expanded')).toBe('true')
    expect(all.getAttribute('aria-controls')).toBe(root.querySelector('[role=menu]')!.id)
    expect(document.activeElement).toBe(items[0])
    items[0]!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }))
    await nextTick(); await nextTick()
    expect(document.activeElement).toBe(items[1])
    items[1]!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    expect(document.activeElement).toBe(all)
    await vi.waitFor(() => expect(root.querySelector('[role=menu]')).toBeNull())
    all.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await nextTick(); await nextTick()
    root.querySelectorAll<HTMLButtonElement>('[role=menuitemradio]')[1]!.click()
    await nextTick()
    await vi.waitFor(() => expect(root.querySelector('[role=menu]')).toBeNull())
    expect(showClosedOnly.value).toBe(true)
    expect(document.activeElement).toBe(all)
    expect(all.textContent).toContain('closedConnections')
    expect(closeButton().disabled).toBe(true)
    all.click()
    await nextTick(); await nextTick()
    expect(document.activeElement).toBe(root.querySelectorAll('[role=menuitemradio]')[1])
    all.click()
    await nextTick()
    await vi.waitFor(() => expect(root.querySelector('[role=menu]')).toBeNull())
    all.click()
    await nextTick(); await nextTick()
    root.querySelectorAll<HTMLButtonElement>('[role=menuitemradio]')[0]!.click()
    await nextTick()
    expect(showClosedOnly.value).toBe(false)
    expect(closeButton().disabled).toBe(false)
  })
  it.each([false, true])('never closes anything from the closed-only view (filtered=%s)', async (filtered) => {
    showClosedOnly.value = true
    await mount([conn('active')], [conn('closed-1'), conn('closed-2')], CONNECTION_TAB_TYPE.ALL)
    if (filtered) renderConnections.value = [closedConnections.value[0]]
    await nextTick()
    await clickClose()
    expect(calls.all).not.toHaveBeenCalled()
    expect(calls.byId).not.toHaveBeenCalled()
    expect(closeButton().disabled).toBe(true)
  })

  it('never invokes either API for empty results', async () => {
    await mount([])
    await clickClose()
    expect(calls.all).not.toHaveBeenCalled()
    expect(calls.byId).not.toHaveBeenCalled()
    expect(closeButton().disabled).toBe(true)
  })

  it('closes all when the visible targets cover every current active connection', async () => {
    await mount([conn('a'), conn('b')], [conn('old')], CONNECTION_TAB_TYPE.ALL)
    await clickClose()
    expect(calls.all).toHaveBeenCalledOnce()
    expect(calls.byId).not.toHaveBeenCalled()
  })

  it('only closes visible active targets when a filtered All view includes history', async () => {
    await mount([conn('a'), conn('b')], [conn('old')], CONNECTION_TAB_TYPE.ALL)
    renderConnections.value = [closedConnections.value[0], activeConnections.value[0]]
    await clickClose()
    expect(calls.all).not.toHaveBeenCalled()
    expect(calls.byId).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('rejects a stale object even if the current active connection reuses its ID', async () => {
    await mount([conn('a', '2026-10-02T00:00:00Z'), conn('b')])
    renderConnections.value = [conn('a'), conn('gone')]
    await nextTick()
    await clickClose()
    expect(calls.all).not.toHaveBeenCalled()
    expect(calls.byId).not.toHaveBeenCalled()
    expect(closeButton().disabled).toBe(true)
  })

  it('cannot mistake duplicate visible targets for coverage of all active connections', async () => {
    await mount([conn('a'), conn('b')])
    renderConnections.value = [activeConnections.value[0], activeConnections.value[0]]
    await clickClose()
    expect(calls.all).not.toHaveBeenCalled()
    expect(calls.byId).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('does not dispatch from a paused snapshot which can no longer prove current identity', async () => {
    await mount([conn('old-snapshot')])
    isPaused.value = true
    await nextTick()
    await clickClose()
    expect(calls.all).not.toHaveBeenCalled()
    expect(calls.byId).not.toHaveBeenCalled()
    expect(closeButton().disabled).toBe(true)
  })

  it('waits for a fresh active list after resuming a paused snapshot', async () => {
    await mount([conn('old-snapshot')])
    isPaused.value = true
    await nextTick()
    expect(closeButton().disabled).toBe(true)
    isPaused.value = false
    await nextTick()
    expect(closeButton().disabled).toBe(true)
    await clickClose()
    expect(calls.all).not.toHaveBeenCalled()
    expect(calls.byId).not.toHaveBeenCalled()
    activeConnections.value = [conn('current')]
    renderConnections.value = activeConnections.value
    await nextTick()
    expect(closeButton().disabled).toBe(false)
    await clickClose()
    expect(calls.all).toHaveBeenCalledOnce()
  })

  it('stops remaining targets when the session changes during dispatch', async () => {
    await mount([conn('a'), conn('b'), conn('hidden')])
    renderConnections.value = activeConnections.value.slice(0, 2)
    calls.byId.mockImplementationOnce(() => { calls.generation++; return Promise.resolve() })
    await clickClose()
    expect(calls.byId).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('rechecks object membership if a target is replaced before its dispatch', async () => {
    await mount([conn('a'), conn('b'), conn('hidden')])
    renderConnections.value = activeConnections.value.slice(0, 2)
    calls.byId.mockImplementationOnce(() => {
      activeConnections.value = [activeConnections.value[0], conn('b', '2026-10-03T00:00:00Z'), activeConnections.value[2]]
      return Promise.resolve()
    })
    await clickClose()
    expect(calls.byId).toHaveBeenCalledExactlyOnceWith('a')
  })

  it('keeps the real manual-request error notification', async () => {
    await mount([conn('a')])
    calls.all.mockRejectedValueOnce(new Error('close failed'))
    await clickClose()
    expect(calls.notify).toHaveBeenCalledWith(expect.objectContaining({ content: 'close failed', type: 'alert-error' }))
  })

  it('does not notify an obsolete session about a late failure', async () => {
    await mount([conn('a')])
    let reject!: (reason: unknown) => void
    calls.all.mockImplementationOnce(() => new Promise((_, fail) => { reject = fail }))
    await clickClose()
    calls.generation++
    reject(new Error('old session failure'))
    await nextTick()
    expect(calls.notify).not.toHaveBeenCalled()
  })
})
