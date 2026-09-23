<template>
  <div
    ref="parentRef"
    class="base-container m-3 h-full overflow-auto backdrop-blur-none!"
  >
    <div class="bg-base-100/80 min-w-min">
      <table :class="['table', sizeOfTable]">
      <thead class="bg-base-100 border-base-300/60 sticky top-0 z-10 border-b backdrop-blur-none!">
        <tr
          v-for="headerGroup in tanstackTable.getHeaderGroups()"
          :key="headerGroup.id"
        >
          <th
            v-for="header in headerGroup.headers"
            :key="header.id"
            :colSpan="header.colSpan"
            :class="[inheritedStyle, header.column.getCanSort() && 'cursor-pointer select-none']"
            @click="header.column.getToggleSortingHandler()?.($event)"
          >
            <div class="flex items-center gap-1 whitespace-nowrap">
              <FlexRender
                v-if="!header.isPlaceholder"
                :render="header.column.columnDef.header"
                :props="header.getContext()"
              />
              <ArrowUpCircleIcon
                v-if="header.column.getIsSorted() === 'asc'"
                class="h-4 w-4"
              />
              <ArrowDownCircleIcon
                v-if="header.column.getIsSorted() === 'desc'"
                class="h-4 w-4"
              />
            </div>
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="rows.length === 0">
          <td
            :colspan="tanstackTable.getVisibleLeafColumns().length"
            class="text-base-content/50 h-90"
          >
            <div class="flex h-full flex-col items-center justify-center gap-3 px-6 text-center">
              <CircleStackIcon class="h-10 w-10 opacity-60" />
              <div class="text-base">{{ t('noData') }}</div>
            </div>
          </td>
        </tr>
        <template v-else>
          <tr
            v-if="paddingTop > 0"
            :style="{ height: `${paddingTop}px` }"
          ></tr>
          <tr
            v-for="virtualRow in virtualRows"
            :key="virtualRow.key.toString()"
            :style="{ height: `${estimateSize}px` }"
            class="hover:bg-primary! hover:text-primary-content!"
            :class="[
              virtualRow.index % 2 === 0 ? 'bg-base-150' : 'bg-base-100',
              rowClass?.(rows[virtualRow.index].original),
            ]"
            @click="emits('rowClick', rows[virtualRow.index].original)"
          >
            <td
              v-for="cell in rows[virtualRow.index].getVisibleCells()"
              :key="cell.id"
              class="max-w-xl truncate text-sm whitespace-nowrap"
              :class="cell.column.columnDef.meta?.cellClass"
              :title="cellTitle(cell)"
              @contextmenu="handleCellRightClick($event, cell)"
            >
              <FlexRender
                v-if="!cell.getIsPlaceholder()"
                :render="cell.column.columnDef.cell"
                :props="cell.getContext()"
              />
            </td>
          </tr>
          <tr
            v-if="paddingBottom > 0"
            :style="{ height: `${paddingBottom}px` }"
          ></tr>
        </template>
      </tbody>
      </table>
    </div>
  </div>
</template>

<script setup lang="ts" generic="T">
import { TABLE_SIZE } from '@/constant'
import { backgroundImage } from '@/helper/indexeddb'
import { showNotification } from '@/helper/notification'
import { tableSize } from '@/store/settings'
import { ArrowDownCircleIcon, ArrowUpCircleIcon, CircleStackIcon } from '@heroicons/vue/24/outline'
import {
  FlexRender,
  getCoreRowModel,
  getSortedRowModel,
  isFunction,
  useVueTable,
  type Cell,
  type ColumnDef,
  type SortingState,
  type VisibilityState,
} from '@tanstack/vue-table'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useDashboardStorage as useStorage } from '@/helper/storage'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

const props = withDefaults(
  defineProps<{
    data: T[]
    columns: ColumnDef<T>[]
    sortingKey: string
    initialSorting?: SortingState
    estimateSize?: number
    overscan?: number
    columnVisibility?: VisibilityState
    rowClass?: (row: T) => string | undefined
    getRowKey?: (row: T, index: number) => string | number
  }>(),
  {
    estimateSize: 36,
    overscan: 24,
  },
)

const emits = defineEmits<{
  (e: 'rowClick', row: T): void
}>()

const { t } = useI18n()
const sorting = useStorage<SortingState>(props.sortingKey, props.initialSorting ?? [])
const rowIdentities = new WeakMap<object, number>()
let nextRowIdentity = 0

const tanstackTable = useVueTable({
  get data() {
    return props.data
  },
  get columns() {
    return props.columns
  },
  getRowId: (row, index) => String(props.getRowKey?.(row, index) ?? defaultRowKey(row, index)),
  state: {
    get sorting() {
      return sorting.value
    },
    get columnVisibility() {
      return props.columnVisibility ?? {}
    },
  },
  onSortingChange: (updater) => {
    sorting.value = isFunction(updater) ? updater(sorting.value) : updater
  },
  getSortedRowModel: getSortedRowModel(),
  getCoreRowModel: getCoreRowModel(),
})

const rows = computed(() => tanstackTable.getRowModel().rows)

function defaultRowKey(row: T, index: number) {
  if (row && typeof row === 'object') {
    const record = row as Record<string, unknown>
    for (const key of ['id', 'uuid', 'seq', 'name']) {
      const value = record[key]
      if (typeof value === 'string' || typeof value === 'number') return `${key}:${value}`
    }
    let identity = rowIdentities.get(row)
    if (identity === undefined) {
      identity = nextRowIdentity++
      rowIdentities.set(row, identity)
    }
    return `object:${identity}`
  }
  return `${typeof row}:${String(row)}:${index}`
}

const parentRef = ref<HTMLElement | null>(null)
const rowVirtualizerOptions = computed(() => ({
  count: rows.value.length,
  getScrollElement: () => parentRef.value,
  estimateSize: () => props.estimateSize,
  getItemKey: (index: number) => rows.value[index]?.id ?? index,
  overscan: props.overscan,
}))
const rowVirtualizer = useVirtualizer(rowVirtualizerOptions)
const virtualRows = computed(() => rowVirtualizer.value.getVirtualItems())
const paddingTop = computed(() => virtualRows.value[0]?.start ?? 0)
const paddingBottom = computed(() => {
  const last = virtualRows.value[virtualRows.value.length - 1]

  return last ? rowVirtualizer.value.getTotalSize() - last.end : 0
})

const sizeOfTable = computed(() => (tableSize.value === TABLE_SIZE.SMALL ? 'table-xs' : 'table-sm'))
const inheritedStyle = computed(() =>
  backgroundImage.value ? 'bg-inherit backdrop-blur-sm' : 'bg-inherit',
)

const cellTitle = (cell: Cell<T, unknown>) => {
  const value = cell.getValue()

  return typeof value === 'string' || typeof value === 'number' ? String(value) : undefined
}

const copyToClipboard = async (text: string) => {
  try {
    await navigator.clipboard.writeText(text)
    showNotification({ content: 'copySuccess', type: 'alert-success', timeout: 2000 })
  } catch {
    const textArea = document.createElement('textarea')
    textArea.value = text
    document.body.appendChild(textArea)
    textArea.select()
    try {
      document.execCommand('copy')
      showNotification({ content: 'copySuccess', type: 'alert-success', timeout: 2000 })
    } finally {
      document.body.removeChild(textArea)
    }
  }
}

const handleCellRightClick = (event: MouseEvent, cell: Cell<T, unknown>) => {
  const value = cellTitle(cell)

  if (value && value !== '-') {
    event.preventDefault()
    copyToClipboard(value)
  }
}
</script>
