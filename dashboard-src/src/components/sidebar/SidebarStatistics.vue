<template>
  <div class="bg-base-200/45 grid w-full grid-cols-2 gap-2 rounded-lg p-2">
    <div
      v-for="stat in statistics"
      :key="stat.label"
      class="flex flex-col items-start"
    >
      <div class="text-base-content/70 text-xs">{{ $t(stat.label) }}</div>
      <div class="text-sm">{{ stat.value }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { prettyBytesHelper } from '@/helper/utils'
import { activeConnections, downloadTotal, uploadTotal } from '@/store/connections'
import { downloadSpeed, memory, uploadSpeed } from '@/store/overview'
import { computed } from 'vue'

const statistics = computed(() => [
  {
    label: 'connections',
    value: activeConnections.value.length,
  },
  {
    label: 'memoryUsage',
    value: prettyBytesHelper(memory.value, { binary: true }),
  },
  {
    label: 'download',
    value: prettyBytesHelper(downloadTotal.value),
  },
  {
    label: 'dlSpeed',
    value: `${prettyBytesHelper(downloadSpeed.value)}/s`,
  },
  {
    label: 'upload',
    value: prettyBytesHelper(uploadTotal.value),
  },
  {
    label: 'ulSpeed',
    value: `${prettyBytesHelper(uploadSpeed.value)}/s`,
  },
])
</script>
