// 组装层 · Clash-compatible rules 门面。
import { toggleRuleDisabledAPI, toggleRuleDisabledSingBoxAPI } from '@/api/clash'
import { RULE_TAB_TYPE } from '@/constant'
import { captureBackendSession } from '@/helper/backendSession'
import { toSearchRegex } from '@/helper/search'
import type { Rule, RuleProvider } from '@/types'
import { computed, ref } from 'vue'

export const rulesFilter = ref('')
export const rulesTabShow = ref(RULE_TAB_TYPE.RULES)

export const rules = ref<Rule[]>([])
export const ruleProviderList = ref<RuleProvider[]>([])

export const renderRules = computed(() => {
  const searchRegex = toSearchRegex(rulesFilter.value)

  if (!searchRegex) {
    return rules.value
  }

  return rules.value.filter((rule) => {
    return searchRegex.testAny([rule.type, rule.payload, rule.proxy])
  })
})

export const renderRulesProvider = computed(() => {
  const searchRegex = toSearchRegex(rulesFilter.value)

  if (!searchRegex) {
    return ruleProviderList.value
  }

  return ruleProviderList.value.filter((ruleProvider) => {
    return searchRegex.testAny([ruleProvider.name, ruleProvider.behavior, ruleProvider.vehicleType])
  })
})

const load = () => import('./clash')

export const fetchRules = async () => {
  const session = captureBackendSession()
  const backend = await load()
  if (session.isCurrent()) return backend.fetchRules()
}

export const resetRules = () => {
  rules.value = []
  ruleProviderList.value = []
}

export const toggleRuleDisabled = (rule: Rule, disabled: boolean) =>
  rule.uuid
    ? toggleRuleDisabledSingBoxAPI(rule.uuid)
    : toggleRuleDisabledAPI({ [rule.index]: disabled })

// 规则启用 / 规则集更新动作(Clash 专属),经 rules 域门面暴露给 view。
export {
  toggleRuleDisabledAPI,
  toggleRuleDisabledSingBoxAPI,
  updateRuleProviderAPI,
} from '@/api/clash'
