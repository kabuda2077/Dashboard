<template>
  <VirtualTable
    :data="logs"
    :columns="columns"
    sorting-key="config/logs-table-sorting"
    :estimate-size="36"
    :row-class="rowClass"
    @row-click="handlerRowClick"
  />
</template>

<script setup lang="ts">
import HighlightText from '@/components/common/HighlightText.vue'
import VirtualTable from '@/components/common/VirtualTable.vue'
import { LOG_LEVEL } from '@/constant'
import { getLogConnectionID, logFilter } from '@/store/logs'
import type { LogWithSeq } from '@/types'
import type { ColumnDef } from '@tanstack/vue-table'
import { h } from 'vue'
import { useI18n } from 'vue-i18n'

defineProps<{ logs: LogWithSeq[] }>()
const emits = defineEmits<{ (e: 'connectionClick', connectionID: string): void }>()
const { t } = useI18n()

const colorMapForType: Record<string, string> = {
  [LOG_LEVEL.Trace]: 'text-success',
  [LOG_LEVEL.Debug]: 'text-accent',
  [LOG_LEVEL.Info]: 'text-info',
  [LOG_LEVEL.Warning]: 'text-warning',
  [LOG_LEVEL.Error]: 'text-error',
  [LOG_LEVEL.Fatal]: 'text-error',
  [LOG_LEVEL.Panic]: 'text-error',
}

const columns: ColumnDef<LogWithSeq>[] = [
  {
    header: '#',
    id: 'seq',
    accessorFn: (log) => log.seq,
    sortingFn: (prev, next) => prev.original.seq - next.original.seq,
    cell: ({ row }) => h('span', { class: 'text-base-content/50 tabular-nums' }, row.original.seq),
    meta: { cellClass: 'w-12' },
  },
  {
    header: () => t('time'),
    id: 'time',
    accessorFn: (log) => log.time,
    cell: ({ row }) => h(HighlightText, { text: row.original.time, filter: logFilter.value }),
    meta: { cellClass: 'w-40' },
  },
  {
    header: () => t('logLevel'),
    id: 'type',
    accessorFn: (log) => log.type,
    cell: ({ row }) =>
      h(
        'span',
        { class: `text-xs tracking-wide uppercase ${colorMapForType[row.original.type] ?? ''}` },
        h(HighlightText, { text: row.original.type, filter: logFilter.value }),
      ),
    meta: { cellClass: 'w-24' },
  },
  {
    header: () => t('content'),
    id: 'payload',
    enableSorting: false,
    accessorFn: (log) => log.payload,
    cell: ({ row }) =>
      h(HighlightText, { text: row.original.payload, filter: logFilter.value, ansi: true }),
    meta: { cellClass: 'max-w-none!' },
  },
]

const rowClass = (log: LogWithSeq) =>
  getLogConnectionID(log.payload) ? 'cursor-pointer' : undefined

const handlerRowClick = (log: LogWithSeq) => {
  const connectionID = getLogConnectionID(log.payload)

  if (connectionID) emits('connectionClick', connectionID)
}
</script>
