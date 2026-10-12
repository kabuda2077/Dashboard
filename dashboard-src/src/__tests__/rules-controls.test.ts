import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'
import type { Rule } from '@/types'

const calls = vi.hoisted(() => ({ toggle: vi.fn(), fetch: vi.fn(), notice: vi.fn() }))
vi.mock('@/assembly/rules', async () => {
  const { ref } = await import('vue')
  const rules = ref<Rule[]>([])
  return {
    rules, renderRules: rules, renderRulesProvider: ref([]), ruleProviderList: ref([]),
    rulesFilter: ref(''), rulesTabShow: ref('rules'),
    fetchRules: calls.fetch, toggleRuleDisabled: calls.toggle, updateRuleProviderAPI: vi.fn(),
  }
})
vi.mock('@/assembly/proxies', async () => ({ proxyGroupList: (await import('vue')).ref([]) }))
vi.mock('@/assembly/connections', () => ({ disconnectByIdAPI: vi.fn() }))
vi.mock('@/store/connections', async () => ({ activeConnections: (await import('vue')).ref([]) }))
vi.mock('@/store/settings', async () => {
  const { ref } = await import('vue')
  return { disconnectOnRuleDisable: ref(false), displayLatencyInRule: ref(false), displayNowNodeInRule: ref(false) }
})
vi.mock('@/helper', () => ({ getConnectionRulePayload: () => '' }))
vi.mock('@/helper/backendSession', () => ({ captureBackendSession: () => ({ isCurrent: () => true }) }))
vi.mock('@/helper/request-error', () => ({ notifyRequestErrorForSession: calls.notice }))
vi.mock('@/helper/tooltip', () => ({ useTooltip: () => ({ showTip: vi.fn() }) }))
vi.mock('@/helper/utils', () => ({ fromNow: () => '' }))
vi.mock('@/composables/use-bounce-on-visible', () => ({ useBounceOnVisible: vi.fn() }))
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('@/components/proxies/ProxyGroup.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/ProxyChainPath.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/HighlightText.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/DialogWrapper.vue', () => ({ default: { render: () => null } }))
vi.mock('@/components/common/VirtualTable.vue', () => ({
  default: defineComponent({
    props: ['data', 'columns'],
    setup(props) {
      return () => h('div', props.data.map((rule: Rule) =>
        props.columns.find((column: { id: string }) => column.id === 'status')?.cell({ row: { original: rule } }),
      ))
    },
  }),
}))

import { rules } from '@/assembly/rules'
import RuleCard from '@/components/rules/RuleCard.vue'
import RulesTable from '@/components/rules/RulesTable.vue'

let app: App | undefined
let container: HTMLDivElement
beforeEach(() => {
  vi.clearAllMocks()
  rules.value = [{ type: 'DOMAIN', payload: 'example.test', proxy: 'DIRECT', uuid: 'rule-1', index: 0, disabled: false } as Rule]
  container = document.createElement('div')
  document.body.append(container)
})
afterEach(() => { app?.unmount(); container.remove() })

for (const [name, component] of [['card', RuleCard], ['table', RulesTable]] as const) {
  describe(name, () => {
    const mount = () => {
      app = createApp({ render: () => h(component, { rule: rules.value[0], index: 1 }) })
      app.mount(container)
      return container.querySelector<HTMLInputElement>('input[type=checkbox]')!
    }

    it('keeps the confirmed value while pending and after a rejected write', async () => {
      let reject!: (error: Error) => void
      calls.toggle.mockImplementationOnce(() => new Promise((_, fail) => { reject = fail }))
      const input = mount()
      expect(input.checked).toBe(true)
      input.click()
      await nextTick()
      expect(input.checked).toBe(true)
      expect(input.disabled).toBe(true)
      input.click()
      expect(calls.toggle).toHaveBeenCalledTimes(1)
      reject(new Error('write rejected'))
      await vi.waitFor(() => expect(input.disabled).toBe(false))
      expect(input.checked).toBe(true)
      expect(calls.notice).toHaveBeenCalledOnce()
    })

    it('shows the refreshed backend value after a successful write', async () => {
      let resolve!: () => void
      calls.toggle.mockImplementationOnce(() => new Promise<void>((done) => { resolve = done }))
      calls.fetch.mockImplementationOnce(async () => { rules.value = [{ ...rules.value[0]!, disabled: true }] })
      const input = mount()
      input.click()
      await nextTick()
      expect(input.checked).toBe(true)
      resolve()
      await vi.waitFor(() => expect(container.querySelector<HTMLInputElement>('input')!.checked).toBe(false))
      expect(container.querySelector<HTMLInputElement>('input')!.disabled).toBe(false)
      expect(calls.notice).not.toHaveBeenCalled()
    })
  })
}
