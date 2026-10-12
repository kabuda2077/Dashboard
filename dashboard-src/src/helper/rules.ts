import { disconnectByIdAPI } from '@/assembly/connections'
import { fetchRules, ruleProviderList, toggleRuleDisabled } from '@/assembly/rules'
import { getConnectionRulePayload } from '@/helper'
import { captureBackendSession } from '@/helper/backendSession'
import { useTooltip } from '@/helper/tooltip'
import { activeConnections } from '@/store/connections'
import { disconnectOnRuleDisable } from '@/store/settings'
import type { Rule } from '@/types'
import dayjs from 'dayjs'
import { useI18n } from 'vue-i18n'

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
      .forEach((connection) => {
        void disconnectByIdAPI(connection.id).catch(() => {})
      })
  }

  await fetchRules()
}

export const EMPTY_CELL = '—'

export const formatRuleHitCount = (count: number | undefined) =>
  count ? count.toLocaleString() : EMPTY_CELL

export const formatRuleHitTime = (
  count: number | undefined,
  at: string | undefined,
  format = 'YYYY-MM-DD HH:mm:ss',
) => {
  if (!count || !at) return ''

  const time = dayjs(at)

  return time.isValid() && time.year() > 1 ? time.format(format) : ''
}

export const useRuleHitTooltip = () => {
  const { t } = useI18n()
  const { showTip } = useTooltip()

  const buildLine = (text: string) => {
    const line = document.createElement('div')

    line.textContent = text

    return line
  }

  const buildSection = (countText: string, count: number, at: string, lastTextKey: string) => {
    const section = document.createElement('div')
    const time = formatRuleHitTime(count, at)

    section.className = 'flex flex-col gap-1'
    section.append(buildLine(countText))
    if (time) {
      section.append(buildLine(t(lastTextKey, { time })))
    }

    return section
  }

  const showRuleHitTip = (event: Event, rule: Rule) => {
    const extra = rule.extra

    if (!extra) return

    const content = document.createElement('div')

    content.className = 'flex flex-col gap-2 text-sm'
    content.append(
      buildSection(
        t('ruleHitCount', { count: extra.hitCount }),
        extra.hitCount,
        extra.hitAt,
        'ruleLastHit',
      ),
      buildSection(
        t('ruleMissCount', { count: extra.missCount }),
        extra.missCount,
        extra.missAt,
        'ruleLastMiss',
      ),
    )

    showTip(event, content, {
      delay: [500, 0],
      trigger: 'mouseenter',
    })
  }

  return { showRuleHitTip }
}
