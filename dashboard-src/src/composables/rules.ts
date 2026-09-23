import { disconnectByIdAPI } from '@/assembly/connections'
import { fetchRules, ruleProviderList, toggleRuleDisabled } from '@/assembly/rules'
import { getConnectionRulePayload } from '@/helper'
import { activeConnections } from '@/store/connections'
import { disconnectOnRuleDisable } from '@/store/settings'
import type { Rule } from '@/types'
import { captureBackendSession } from '@/helper/backendSession'

export const isRuleDisabled = (rule: Rule) => (rule.extra ? rule.extra.disabled : rule.disabled)

export const getRuleSize = (rule: Rule) => {
  if (rule.type === 'RuleSet') {
    return ruleProviderList.value.find((provider) => provider.name === rule.payload)?.ruleCount
  }

  return rule.size
}

export const isUpdateableRuleSet = (rule: Rule) => {
  if (rule.type !== 'RuleSet') return false

  const provider = ruleProviderList.value.find((item) => item.name === rule.payload)

  return Boolean(provider && provider.vehicleType !== 'Inline')
}

export const toggleRuleDisabledWithSideEffects = async (rule: Rule) => {
  const session = captureBackendSession()
  const willBeDisabled = !isRuleDisabled(rule)

  await toggleRuleDisabled(rule, willBeDisabled)
  if (!session.isCurrent()) return

  if (willBeDisabled && disconnectOnRuleDisable.value) {
    activeConnections.value
      .filter(
        (connection) =>
          connection.rule === rule.type &&
          getConnectionRulePayload(connection) === (rule.payload || ''),
      )
      .forEach((connection) => { void disconnectByIdAPI(connection.id).catch(() => {}) })
  }

  await fetchRules()
}
