// Clash REST 后端的 rules 组装:拉取 /rules 与 /providers/rules,写入门面状态。
import { fetchRuleProvidersAPI, fetchRulesAPI } from '@/api/clash'
import { ruleProviderList, rules } from './index'
import { captureBackendSession } from '@/helper/backendSession'

export const fetchRules = async () => {
  const session = captureBackendSession()
  const [{ data: ruleData }, { data: providerData }] = await Promise.all([
    fetchRulesAPI(), fetchRuleProvidersAPI(),
  ])
  if (!session.isCurrent()) return

  rules.value = ruleData.rules.map((rule) => {
    const proxy = rule.proxy
    const proxyName = proxy.startsWith('route(') ? proxy.substring(6, proxy.length - 1) : proxy

    return {
      ...rule,
      proxy: proxyName,
    }
  })
  ruleProviderList.value = Object.values(providerData.providers)
}
