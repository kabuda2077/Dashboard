import { ruleProviderList, toggleRuleDisabled } from '@/assembly/rules'
import { getRuleSize, isRuleDisabled, isUpdateableRuleSet } from '@/composables/rules'
import type { Rule, RuleProvider } from '@/types'
import { afterEach, describe, expect, it, vi } from 'vitest'

const apiMocks = vi.hoisted(() => ({
  toggleRuleDisabledAPI: vi.fn(),
  toggleRuleDisabledSingBoxAPI: vi.fn(),
  updateRuleProviderAPI: vi.fn(),
}))

vi.mock('@/api/clash', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/api/clash')>()),
  ...apiMocks,
}))

const createRule = (overrides: Partial<Rule> = {}) =>
  ({
    type: 'DOMAIN',
    payload: 'example.test',
    proxy: 'Proxy',
    size: 1,
    uuid: '',
    index: 1,
    ...overrides,
  }) as Rule

afterEach(() => {
  ruleProviderList.value = []
  vi.clearAllMocks()
})

describe('shared rule behavior', () => {
  it('prefers the backend-specific extra disabled state', () => {
    const rule = createRule({
      disabled: true,
      extra: {
        disabled: false,
        hitAt: '',
        hitCount: 0,
        missAt: '',
        missCount: 0,
      },
    })

    expect(isRuleDisabled(rule)).toBe(false)
  })

  it('reads RuleSet size and update capability from its provider', () => {
    ruleProviderList.value = [
      {
        name: 'remote-rules',
        ruleCount: 42,
        vehicleType: 'HTTP',
      } as RuleProvider,
    ]
    const rule = createRule({ type: 'RuleSet', payload: 'remote-rules', size: -1 })

    expect(getRuleSize(rule)).toBe(42)
    expect(isUpdateableRuleSet(rule)).toBe(true)
  })

  it('keeps inline RuleSet providers read-only', () => {
    ruleProviderList.value = [
      {
        name: 'inline-rules',
        ruleCount: 3,
        vehicleType: 'Inline',
      } as RuleProvider,
    ]

    expect(isUpdateableRuleSet(createRule({ type: 'RuleSet', payload: 'inline-rules' }))).toBe(
      false,
    )
  })

  it('routes rule toggles through the backend-compatible identifier', async () => {
    const mihomoRule = createRule({ index: 7 })
    const singBoxRule = createRule({ uuid: 'rule-uuid' })

    await toggleRuleDisabled(mihomoRule, true)
    await toggleRuleDisabled(singBoxRule, false)

    expect(apiMocks.toggleRuleDisabledAPI).toHaveBeenCalledWith({ 7: true })
    expect(apiMocks.toggleRuleDisabledSingBoxAPI).toHaveBeenCalledWith('rule-uuid')
  })
})
