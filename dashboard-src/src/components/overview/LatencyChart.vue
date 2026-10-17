<template>
  <div class="flex w-full min-w-0 items-center gap-2">
    <span
      class="w-14 shrink-0 text-sm leading-none font-medium tabular-nums"
      :class="median ? getColorForLatency(median) : 'text-base-content/40'"
      :aria-label="summary"
    >
      {{ median ? `${median}ms` : '--' }}
    </span>
    <div
      class="flex min-w-0 flex-wrap gap-1.5"
      :aria-label="summary"
      role="img"
    >
      <span
        v-for="(value, index) in samples"
        :key="index"
        class="h-1.5 w-1.5 shrink-0 rounded-full"
        :class="sampleColor(value)"
        :title="sampleTip(value, index)"
        aria-hidden="true"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { getColorForLatency } from '@/helper'
import { lowLatency, mediumLatency } from '@/store/settings'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

const props = withDefaults(defineProps<{ data: number[]; rounds?: number; name?: string }>(), {
  rounds: 5,
  name: 'Latency',
})
const { t } = useI18n()
const samples = computed(() =>
  Array.from({ length: props.rounds }, (_, index) => props.data[index]),
)
const successes = computed(() => props.data.filter((value) => value > 0))
const median = computed(() => {
  const sorted = [...successes.value].sort((a, b) => a - b)
  if (!sorted.length) return 0
  const middle = Math.floor(sorted.length / 2)
  return Math.round(sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2)
})
const summary = computed(() =>
  t('networkSamples', {
    success: successes.value.length,
    total: props.data.length,
    rounds: props.rounds,
  }),
)
const sampleColor = (value: number | undefined) => {
  if (value === undefined) return 'bg-base-content/15'
  if (value <= 0) return 'bg-error'
  if (value < lowLatency.value) return 'bg-low-latency'
  if (value < mediumLatency.value) return 'bg-medium-latency'
  return 'bg-high-latency'
}
const sampleTip = (value: number | undefined, index: number) =>
  `${props.name} · ${index + 1}/${props.rounds} · ${value === undefined ? t('networkNotTested') : value > 0 ? `${value}ms` : t('networkRequestFailed')}`
</script>
