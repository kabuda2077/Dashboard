import { fetchRulesAPI } from '@/api/clash'
import { resetRules, rules } from '@/assembly/rules'
import { fetchRules } from '@/assembly/rules/clash'
import { expect, it, vi } from 'vitest'
vi.mock('@/api/clash', () => ({
  fetchRulesAPI: vi.fn(),
  fetchRuleProvidersAPI: vi.fn(async () => ({ data: { providers: {} } })),
  toggleRuleDisabledAPI: vi.fn(),
  toggleRuleDisabledSingBoxAPI: vi.fn(),
  updateRuleProviderAPI: vi.fn(),
}))
vi.mock('@/helper/backendSession', () => ({
  captureBackendSession: () => ({ isCurrent: () => true }),
}))
it('same-epoch reset invalidates pending rules instead of resurrecting the cleared state', async () => {
  let finish!: (value: unknown) => void
  vi.mocked(fetchRulesAPI).mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }) as never,
  )
  const pending = fetchRules()
  resetRules()
  finish({ data: { rules: [{ type: 'MATCH', payload: '', proxy: 'DIRECT' }] } })
  await pending
  expect(rules.value).toEqual([])
})
