import type { Rule, RuleProvider } from '@/types'
import {
  createTable,
  getCoreRowModel,
  getSortedRowModel,
  type ColumnDef,
} from '@tanstack/vue-table'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App, type Component } from 'vue'

const calls = vi.hoisted(() => ({ tip: vi.fn(), update: vi.fn(), fetch: vi.fn(), notice: vi.fn() }))
vi.mock('@/assembly/rules', async () => {
  const { ref } = await import('vue')
  const rules = ref<Rule[]>([])
  return {
    rules,
    renderRules: rules,
    renderRulesProvider: ref([]),
    ruleProviderList: ref([]),
    rulesFilter: ref(''),
    rulesTabShow: ref('rules'),
    fetchRules: calls.fetch,
    updateRuleProviderAPI: calls.update,
    toggleRuleDisabled: vi.fn(),
  }
})
vi.mock('@/assembly/proxies', async () => ({ proxyGroupList: (await import('vue')).ref([]) }))
vi.mock('@/assembly/connections', () => ({ disconnectByIdAPI: vi.fn() }))
vi.mock('@/store/connections', async () => ({ activeConnections: (await import('vue')).ref([]) }))
vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  return {
    disconnectOnRuleDisable: ref(false),
    displayLatencyInRule: ref(false),
    displayNowNodeInRule: ref(false),
  }
})
vi.mock('@/helper', () => ({ getConnectionRulePayload: () => '' }))
vi.mock('@/helper/backendSession', () => ({
  captureBackendSession: () => ({ isCurrent: () => true }),
}))
vi.mock('@/helper/requestError', () => ({ notifyRequestErrorForSession: calls.notice }))
vi.mock('@/helper/tooltip', () => ({ useTooltip: () => ({ showTip: calls.tip }) }))
vi.mock('@/helper/utils', () => ({ fromNow: () => '' }))
vi.mock('@/composables/bouncein', () => ({ useBounceOnVisible: vi.fn() }))
vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, values: Record<string, unknown> = {}) =>
      `${key}:${Object.values(values).join('')}`,
  }),
}))
vi.mock('@/components/proxies/ProxyGroup.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/ProxyChainPath.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/HighlightText.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/DialogWrapper.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/VirtualTable.vue', () => ({
  default: defineComponent({
    props: ['data', 'columns', 'columnVisibility'],
    setup(props) {
      return () => {
        tableColumns = props.columns
        columnVisibility = props.columnVisibility
        return h(
          'div',
          props.data.map((rule: Rule) =>
            h(
              'div',
              props.columns.map((column: ColumnDef<Rule> & { id: string }) =>
                typeof column.cell === 'function'
                  ? h('div', { 'data-column': column.id }, [
                      column.cell({
                        row: { original: rule },
                        getValue: () =>
                          'accessorFn' in column ? column.accessorFn?.(rule, 0) : undefined,
                      } as never),
                    ])
                  : null,
              ),
            ),
          ),
        )
      }
    },
  }),
}))

import { ruleProviderList, rules } from '@/assembly/rules'
import RuleCard from '@/components/rules/RuleCard.vue'
import RulesTable from '@/components/rules/RulesTable.vue'
import { formatRuleHitCount, useRuleHitTooltip } from '@/composables/rules'

let tableColumns: ColumnDef<Rule>[]
let columnVisibility: Record<string, boolean>
let app: App | undefined
let container: HTMLDivElement
const extra = (overrides: Partial<NonNullable<Rule['extra']>> = {}) => ({
  disabled: false,
  hitAt: '',
  hitCount: 0,
  missAt: '',
  missCount: 0,
  ...overrides,
})
const rule = (overrides: Partial<Rule> = {}) =>
  ({
    type: 'DOMAIN',
    payload: 'example.test',
    proxy: 'DIRECT',
    index: 0,
    ...overrides,
  }) as Rule
const mount = (component: Component = RulesTable) => {
  app = createApp({ render: () => h(component, { rule: rules.value[0], index: 1 }) })
  app.mount(container)
}

beforeEach(() => {
  vi.clearAllMocks()
  rules.value = []
  ruleProviderList.value = []
  container = document.createElement('div')
  document.body.append(container)
})
afterEach(() => {
  app?.unmount()
  app = undefined
  container.remove()
})

describe('shared rule hit tooltip', () => {
  it('uses plain text for unsafe count strings and includes supplied timestamps', () => {
    const unsafe = '<img src=x onerror=alert(1)>'
    const testRule = rule({
      extra: extra({
        hitCount: unsafe as unknown as number,
        hitAt: '2025-01-02T03:04:05',
        missCount: unsafe as unknown as number,
        missAt: '2025-02-03T04:05:06',
      }),
    })
    useRuleHitTooltip().showRuleHitTip(new MouseEvent('mouseenter'), testRule)
    const content = calls.tip.mock.calls[0]![1] as HTMLElement
    expect(content.querySelector('img')).toBeNull()
    expect([...content.querySelectorAll('div > div')].map((line) => line.textContent)).toContain(
      `ruleHitCount:${unsafe}`,
    )
    expect(content.textContent).toContain('ruleLastHit:2025-01-02 03:04:05')
    expect(content.textContent).toContain(`ruleMissCount:${unsafe}`)
    expect(content.textContent).toContain('ruleLastMiss:2025-02-03 04:05:06')
    expect(content.hasAttribute('data-v-app')).toBe(false)
    expect(content.querySelector('[data-v-app]')).toBeNull()
  })

  it.each(['', undefined])(
    'omits missing timestamps (%s) instead of inventing a last hit or miss time',
    (at) => {
      const show = useRuleHitTooltip().showRuleHitTip
      show(new MouseEvent('mouseenter'), rule({ extra: extra({ hitAt: at, missAt: at }) }))
      const content = calls.tip.mock.calls[0]![1] as HTMLElement
      expect(content.textContent).toBe('ruleHitCount:0ruleMissCount:0')
      show(new MouseEvent('mouseenter'), rule())
      expect(calls.tip).toHaveBeenCalledOnce()
      expect(formatRuleHitCount(0)).toBe('—')
      expect(formatRuleHitCount(undefined)).toBe('—')
      expect(formatRuleHitCount(1234)).toBe((1234).toLocaleString())
    },
  )
})

it('uses the shared static tooltip from the rule card', () => {
  rules.value = [rule({ extra: extra({ hitCount: 4 }) })]
  mount(RuleCard)
  container.querySelector('svg')!.dispatchEvent(new MouseEvent('mouseenter'))
  expect((calls.tip.mock.calls[0]![1] as HTMLElement).textContent).toBe(
    'ruleHitCount:4ruleMissCount:0',
  )
})

describe('rule table statistics', () => {
  it('retains separate numeric Hit and Miss sorting and the visible hit timestamp', () => {
    rules.value = [
      rule({
        payload: 'a',
        extra: extra({ hitCount: 10, missCount: 2, hitAt: '2025-01-02T03:04:05' }),
      }),
      rule({ payload: 'b', extra: extra({ hitCount: 2, missCount: 10 }) }),
    ]
    mount()
    expect(columnVisibility).toMatchObject({ hitCount: true, missCount: true })
    for (const [id, expected] of [
      ['hitCount', ['b', 'a']],
      ['missCount', ['a', 'b']],
    ] as const) {
      const table = createTable({
        data: rules.value,
        columns: tableColumns,
        state: { sorting: [{ id, desc: false }] },
        onStateChange: () => {},
        renderFallbackValue: null,
        getCoreRowModel: getCoreRowModel(),
        getSortedRowModel: getSortedRowModel(),
      })
      expect(table.getColumn(id)?.getCanSort()).toBe(true)
      expect(table.getRowModel().rows.map((row) => row.original.payload)).toEqual(expected)
      expect(table.getColumn(id)?.columnDef.meta).toMatchObject({
        headerClass: 'text-right',
        noCellTitle: true,
      })
      container
        .querySelector(`[data-column=${id}] span`)!
        .dispatchEvent(new MouseEvent('mouseenter'))
    }
    expect(calls.tip).toHaveBeenCalledTimes(2)
    expect(container.querySelector('[data-column=hitCount]')!.textContent).toContain('01-02 03:04')
    expect(container.querySelectorAll('[data-column=hitCount]')[1]!.textContent).toBe('2')
    expect(tableColumns.some((column) => column.id === 'hitMiss')).toBe(false)
  })

  it('hides both statistics columns for a backend without rule extras', () => {
    rules.value = [rule()]
    mount()
    expect(columnVisibility).toMatchObject({ hitCount: false, missCount: false })
  })
})

for (const [name, component] of [
  ['card', RuleCard],
  ['table', RulesTable],
] as const) {
  it(`${name} disables refresh while pending and reports a rejected request`, async () => {
    rules.value = [rule({ type: 'RuleSet', payload: 'remote' })]
    ruleProviderList.value = [{ name: 'remote', vehicleType: 'HTTP' } as RuleProvider]
    let reject!: (error: Error) => void
    calls.update.mockImplementationOnce(
      () =>
        new Promise((_, fail) => {
          reject = fail
        }),
    )
    mount(component)
    const button = container.querySelector<HTMLButtonElement>('button')!
    button.click()
    await nextTick()
    expect(button.disabled).toBe(true)
    button.click()
    expect(calls.update).toHaveBeenCalledOnce()
    reject(new Error('update rejected'))
    await vi.waitFor(() => expect(button.disabled).toBe(false))
    expect(calls.notice).toHaveBeenCalledOnce()
    expect(calls.fetch).not.toHaveBeenCalled()
  })
}
