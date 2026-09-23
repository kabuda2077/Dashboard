<template>
  <VirtualTable
    v-if="rulesTabShow === RULE_TAB_TYPE.PROVIDER"
    key="rule-providers"
    :data="renderRulesProvider"
    :columns="providerColumns"
    sorting-key="config/rule-providers-table-sorting"
    :estimate-size="36"
    :get-row-key="(provider: RuleProvider) => provider.name"
  />
  <VirtualTable
    v-else
    key="rules"
    :data="renderRules"
    :columns="ruleColumns"
    :column-visibility="ruleColumnVisibility"
    sorting-key="config/rules-table-sorting"
    :estimate-size="36"
    :get-row-key="ruleRowKey"
    :row-class="ruleRowClass"
    @row-click="handlerRuleClick"
  />
  <DialogWrapper
    v-model="groupDialogVisible"
    :title="groupDialogTitle"
  >
    <div
      v-if="selectedRule"
      class="flex flex-col gap-2"
    >
      <ProxyChainPath
        :proxy="selectedRule.proxy"
        :selected="selectedGroup"
        :show-now-node="displayNowNodeInRule"
        :show-latency="displayLatencyInRule"
        @update:selected="selectedGroup = $event"
      />
      <ProxyGroup
        :name="selectedGroup"
        :force-open="true"
        class="transparent-collapse"
      />
    </div>
  </DialogWrapper>
</template>

<script setup lang="ts">
import { captureBackendSession } from '@/helper/backendSession'
import { notifyRequestErrorForSession } from '@/helper/requestError'
import { proxyGroupList } from '@/assembly/proxies'
import {
  fetchRules,
  renderRules,
  renderRulesProvider,
  rules,
  rulesFilter,
  rulesTabShow,
  updateRuleProviderAPI,
} from '@/assembly/rules'
import DialogWrapper from '@/components/common/DialogWrapper.vue'
import HighlightText from '@/components/common/HighlightText.vue'
import ProxyChainPath from '@/components/common/ProxyChainPath.vue'
import VirtualTable from '@/components/common/VirtualTable.vue'
import ProxyGroup from '@/components/proxies/ProxyGroup.vue'
import {
  getRuleSize,
  isRuleDisabled,
  isUpdateableRuleSet,
  toggleRuleDisabledWithSideEffects,
} from '@/composables/rules'
import { RULE_TAB_TYPE } from '@/constant'
import { fromNow } from '@/helper/utils'
import { displayLatencyInRule, displayNowNodeInRule } from '@/store/settings'
import type { Rule, RuleProvider } from '@/types'
import { ArrowPathIcon } from '@heroicons/vue/24/outline'
import type { ColumnDef } from '@tanstack/vue-table'
import dayjs from 'dayjs'
import { computed, h, ref } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const ruleIndexMap = computed(() => new Map(rules.value.map((rule, index) => [rule, index + 1])))
const hasRuleExtra = computed(() => rules.value.some((rule) => rule.extra))
const ruleColumnVisibility = computed(() => ({
  hitCount: hasRuleExtra.value,
  missCount: hasRuleExtra.value,
}))
const ruleRowKey = (rule: Rule) => rule.uuid || `${rule.type}:${rule.payload}:${rule.proxy}`
const updatingProviders = ref<string[]>([])
const togglingRules = ref<string[]>([])
const selectedRule = ref<Rule | null>(null)
const selectedGroup = ref('')
const groupDialogVisible = ref(false)
const groupDialogTitle = computed(() => {
  const rule = selectedRule.value

  return rule ? (rule.payload ? `${rule.type}: ${rule.payload}` : rule.type) : ''
})

const isRuleSelectable = (rule: Rule) =>
  proxyGroupList.value.includes(rule.proxy) && !isRuleDisabled(rule)
const ruleRowClass = (rule: Rule) => {
  const classes = []

  if (isRuleDisabled(rule)) classes.push('opacity-50')
  if (isRuleSelectable(rule)) classes.push('cursor-pointer')

  return classes.join(' ') || undefined
}

const handlerRuleClick = (rule: Rule) => {
  if (!isRuleSelectable(rule)) return

  selectedRule.value = rule
  selectedGroup.value = rule.proxy
  groupDialogVisible.value = true
}

const updateProviderHandler = async (name: string) => {
  if (updatingProviders.value.includes(name)) return

  updatingProviders.value.push(name)
  const session = captureBackendSession()
  try {
    await updateRuleProviderAPI(name)
    if (session.isCurrent()) await fetchRules()
  } catch (error) {
    notifyRequestErrorForSession(error, session)
  } finally {
    updatingProviders.value = updatingProviders.value.filter((item) => item !== name)
  }
}

const toggleRuleHandler = async (rule: Rule) => {
  const key = `${rule.type}-${rule.payload}`

  if (togglingRules.value.includes(key)) return
  togglingRules.value.push(key)
  const session = captureBackendSession()
  try {
    await toggleRuleDisabledWithSideEffects(rule)
  } catch (error) {
    notifyRequestErrorForSession(error, session)
  } finally {
    togglingRules.value = togglingRules.value.filter((item) => item !== key)
  }
}

const updateButton = (name: string, onClick: () => void) =>
  h(
    'button',
    {
      class: `btn btn-circle btn-ghost btn-xs ${updatingProviders.value.includes(name) ? 'animate-spin' : ''}`,
      onClick: (event: MouseEvent) => {
        event.stopPropagation()
        onClick()
      },
    },
    h(ArrowPathIcon, { class: 'h-3.5 w-3.5 opacity-60' }),
  )

const ruleColumns: ColumnDef<Rule>[] = [
  {
    header: '#',
    id: 'index',
    accessorFn: (rule) => ruleIndexMap.value.get(rule) ?? 0,
    cell: ({ row }) =>
      h(
        'span',
        { class: 'text-base-content/50 tabular-nums' },
        ruleIndexMap.value.get(row.original) ?? '',
      ),
    meta: { cellClass: 'w-12' },
  },
  {
    header: () => t('type'),
    id: 'type',
    accessorFn: (rule) => rule.type,
    cell: ({ row }) => h(HighlightText, { text: row.original.type, filter: rulesFilter.value }),
    meta: { cellClass: 'w-40' },
  },
  {
    header: () => t('content'),
    id: 'payload',
    accessorFn: (rule) => rule.payload,
    cell: ({ row }) =>
      row.original.payload
        ? h(HighlightText, { text: row.original.payload, filter: rulesFilter.value })
        : h('span', { class: 'text-base-content/40' }, '-'),
  },
  {
    header: () => t('proxyGroup'),
    id: 'proxy',
    accessorFn: (rule) => rule.proxy,
    cell: ({ row }) =>
      h(ProxyChainPath, {
        proxy: row.original.proxy,
        collapsed: true,
        interactive: false,
        showNowNode: displayNowNodeInRule.value,
        showLatency: displayLatencyInRule.value,
        filter: rulesFilter.value,
      }),
  },
  {
    header: () => t('ruleCount'),
    id: 'size',
    accessorFn: (rule) => {
      const size = getRuleSize(rule)

      return typeof size === 'number' && size !== -1 ? size : ''
    },
    cell: ({ getValue }) => h('span', { class: 'tabular-nums' }, String(getValue() ?? '')),
    meta: { cellClass: 'w-24' },
  },
  {
    header: () => t('hitCount'),
    id: 'hitCount',
    accessorFn: (rule) => rule.extra?.hitCount ?? 0,
    cell: ({ row }) =>
      h('span', { class: 'tabular-nums' }, [
        String(row.original.extra?.hitCount ?? 0),
        row.original.extra?.hitAt
          ? h(
              'span',
              { class: 'text-base-content/40 ml-1 text-xs' },
              dayjs(row.original.extra.hitAt).format('MM-DD HH:mm'),
            )
          : null,
      ]),
    meta: { cellClass: 'w-40' },
  },
  {
    header: () => t('missCount'),
    id: 'missCount',
    accessorFn: (rule) => rule.extra?.missCount ?? 0,
    cell: ({ getValue }) => h('span', { class: 'tabular-nums' }, String(getValue() ?? 0)),
    meta: { cellClass: 'w-24' },
  },
  {
    header: () => t('statusLabel'),
    id: 'status',
    enableSorting: false,
    cell: ({ row }) => {
      const rule = row.original
      if (!rule.uuid && !rule.extra) return null

      return h('input', {
        type: 'checkbox',
        class: 'toggle toggle-sm',
        checked: !isRuleDisabled(rule),
        disabled: togglingRules.value.includes(`${rule.type}-${rule.payload}`),
        onClick: (event: MouseEvent) => event.stopPropagation(),
        onChange: () => toggleRuleHandler(rule),
      })
    },
    meta: { cellClass: 'w-20' },
  },
  {
    header: () => t('actions'),
    id: 'actions',
    enableSorting: false,
    cell: ({ row }) =>
      isUpdateableRuleSet(row.original)
        ? updateButton(row.original.payload, () => updateProviderHandler(row.original.payload))
        : null,
    meta: { cellClass: 'w-20' },
  },
]

const providerColumns: ColumnDef<RuleProvider>[] = [
  {
    header: '#',
    id: 'index',
    accessorFn: (provider) => renderRulesProvider.value.indexOf(provider) + 1,
    cell: ({ getValue }) =>
      h('span', { class: 'text-base-content/50 tabular-nums' }, String(getValue() ?? '')),
    meta: { cellClass: 'w-12' },
  },
  {
    header: () => t('name'),
    id: 'name',
    accessorFn: (provider) => provider.name,
    cell: ({ row }) => h(HighlightText, { text: row.original.name, filter: rulesFilter.value }),
  },
  {
    header: () => t('ruleCount'),
    id: 'ruleCount',
    accessorFn: (provider) => provider.ruleCount,
    cell: ({ getValue }) => h('span', { class: 'tabular-nums' }, String(getValue() ?? 0)),
    meta: { cellClass: 'w-24' },
  },
  {
    header: () => t('behavior'),
    id: 'behavior',
    accessorFn: (provider) => provider.behavior,
    cell: ({ row }) => h(HighlightText, { text: row.original.behavior, filter: rulesFilter.value }),
    meta: { cellClass: 'w-40' },
  },
  {
    header: () => t('vehicleType'),
    id: 'vehicleType',
    accessorFn: (provider) => provider.vehicleType,
    cell: ({ row }) =>
      h(HighlightText, { text: row.original.vehicleType, filter: rulesFilter.value }),
    meta: { cellClass: 'w-32' },
  },
  {
    header: () => t('updated'),
    id: 'updatedAt',
    accessorFn: (provider) => fromNow(provider.updatedAt),
    sortingFn: (prev, next) =>
      dayjs(prev.original.updatedAt).valueOf() - dayjs(next.original.updatedAt).valueOf(),
    cell: ({ getValue }) => h('span', {}, String(getValue() ?? '')),
    meta: { cellClass: 'w-40' },
  },
  {
    header: () => t('actions'),
    id: 'actions',
    enableSorting: false,
    cell: ({ row }) =>
      row.original.vehicleType === 'Inline'
        ? null
        : updateButton(row.original.name, () => updateProviderHandler(row.original.name)),
    meta: { cellClass: 'w-20' },
  },
]
</script>
