<template>
  <div class="flex h-8 min-w-0 items-center gap-2">
    <div class="relative h-full min-w-0 flex-1">
      <MiniSparkline
        :data="chartData"
        :min="lowLatency"
        :color="chartColor"
        :name="name"
        show-symbols
      />
      <div
        v-for="hit in sampleHits"
        :key="hit.index"
        class="absolute top-0 bottom-0 z-10 w-4 -translate-x-1/2 cursor-default"
        :style="{ left: `${hit.left}%` }"
        @mouseenter="showTip($event, `${hit.value}ms`)"
      />
    </div>
    <span
      class="w-11 shrink-0 text-left text-[11px] leading-none tabular-nums"
      :class="showAvg ? avgTextClass : 'text-base-content/30'"
    >
      {{ showAvg ? `${avgLatency}ms` : '--' }}
    </span>
  </div>
</template>

<script setup lang="ts">
import { useTooltip } from '@/helper/tooltip'
import { lowLatency, mediumLatency } from '@/store/settings'
import { computed } from 'vue'
import MiniSparkline from './MiniSparkline.vue'

const props = withDefaults(defineProps<{ data: number[]; rounds?: number; name?: string }>(), {
  rounds: 5,
  name: 'Latency',
})

const { showTip } = useTooltip()

const okValues = computed(() => props.data.filter((v) => v > 0))
const liveAvgLatency = computed(() => {
  if (!okValues.value.length) return 0
  return Math.round(okValues.value.reduce((sum, v) => sum + v, 0) / okValues.value.length)
})
const showAvg = computed(() => props.data.length >= props.rounds && okValues.value.length > 0)
const avgLatency = computed(() => (showAvg.value ? liveAvgLatency.value : 0))

const latencyTone = computed(() => liveAvgLatency.value || 0)

const avgTextClass = computed(() => {
  if (avgLatency.value < lowLatency.value) return 'text-low-latency'
  if (avgLatency.value < mediumLatency.value) return 'text-medium-latency'
  return 'text-high-latency'
})

const chartColor = computed(() => {
  if (!latencyTone.value || latencyTone.value < lowLatency.value) return 'lowLatency'
  if (latencyTone.value < mediumLatency.value) return 'mediumLatency'
  return 'highLatency'
})

const chartData = computed(() =>
  props.data
    .slice(0, props.rounds)
    .filter((value) => value > 0)
    .map((value, index) => ({ name: index, value })),
)

const sampleHits = computed(() =>
  props.data
    .slice(0, props.rounds)
    .map((value, index) => ({ index, value }))
    .filter((item) => item.value > 0)
    .map((item) => ({
      ...item,
      left: props.rounds <= 1 ? 50 : (item.index / (props.rounds - 1)) * 100,
    })),
)
</script>
