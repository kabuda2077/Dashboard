<template>
  <div class="flex flex-col items-center gap-1 py-2">
    <div class="flex w-full flex-col items-center gap-1">
      <template
        v-for="(item, index) in statItems"
        :key="item.key"
      >
        <div
          v-if="index > 0"
          class="bg-base-content/8 my-1 h-px w-6"
        />
        <div class="flex flex-col items-center gap-1 py-1">
          <component
            :is="item.icon"
            class="h-3.5 w-3.5"
            :class="item.iconColor ?? 'text-base-content/60'"
          />
          <span class="text-base-content/90 text-xs leading-tight font-medium tabular-nums">
            {{ item.value }}
          </span>
          <span
            v-if="item.secondaryValue"
            class="text-base-content/60 text-xs leading-tight tabular-nums"
          >
            {{ item.secondaryValue }}
          </span>
        </div>
      </template>
    </div>

    <div class="mt-1 flex flex-col items-center">
      <slot></slot>
    </div>
  </div>
</template>

<script setup lang="ts">
import { prettyBytesHelper } from '@/helper/utils'
import { activeConnectionCount, downloadTotal, uploadTotal } from '@/store/connections'
import { downloadSpeed, memory, uploadSpeed } from '@/store/overview'
import {
  ArrowDownCircleIcon,
  ArrowsRightLeftIcon,
  ArrowUpCircleIcon,
  CpuChipIcon,
} from '@heroicons/vue/24/outline'
import { computed } from 'vue'

const statItems = computed(() => {
  return [
    {
      key: 'connections',
      icon: ArrowsRightLeftIcon,
      value: activeConnectionCount.value,
    },
    {
      key: 'download',
      icon: ArrowDownCircleIcon,
      iconColor: 'text-primary/70',
      value: `${prettyBytesHelper(downloadSpeed.value, { maximumFractionDigits: 1 })}/s`,
      secondaryValue: prettyBytesHelper(downloadTotal.value, { maximumFractionDigits: 1 }),
    },
    {
      key: 'upload',
      icon: ArrowUpCircleIcon,
      iconColor: 'text-info/71',
      value: `${prettyBytesHelper(uploadSpeed.value, { maximumFractionDigits: 1 })}/s`,
      secondaryValue: prettyBytesHelper(uploadTotal.value, { maximumFractionDigits: 1 }),
    },
    {
      key: 'memory',
      icon: CpuChipIcon,
      value: prettyBytesHelper(memory.value, { binary: true, maximumFractionDigits: 1 }),
    },
  ]
})
</script>
