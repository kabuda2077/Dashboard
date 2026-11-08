<template>
  <div class="network-latency-chart">
    <div class="network-latency-bottom">
      <span
        class="network-latency-metric"
        :class="median ? getColorForLatency(median) : 'text-base-content/40'"
        :aria-label="summary"
      >
        <template v-if="median">
          <span class="network-latency-number">{{ median }}</span>
          <span class="network-latency-unit">ms</span>
        </template>
        <span
          v-else
          class="text-lg leading-6 font-light"
        >
          --
        </span>
      </span>
      <div
        class="network-latency-bars"
        :aria-label="sampleSummary"
        role="img"
      >
        <span
          v-for="(value, index) in samples"
          :key="`${attempt}-${index}`"
          class="network-latency-bar"
          :class="[
            sampleColor(value),
            {
              'network-latency-bar-grow':
                progress !== undefined && value !== undefined && index >= animationStart,
            },
          ]"
          :style="{ height: sampleHeight(value) }"
          :title="sampleTip(value, index)"
          aria-hidden="true"
        />
      </div>
    </div>
    <div class="network-tile-track">
      <div
        class="network-tile-track-fill"
        :class="trackColor"
        :style="{ width: trackWidth }"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { getColorForLatency } from '@/helper'
import { lowLatency, mediumLatency } from '@/store/settings'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = withDefaults(
  defineProps<{
    data: number[]
    progress?: number[]
    attempt?: number
    rounds?: number
    name?: string
  }>(),
  { attempt: 0, rounds: 5, name: 'Latency' },
)
// Already visible samples stay still when a page is restored or a test is remounted.
const animationStart = ref(props.progress?.length ?? props.rounds)
watch(
  () => props.attempt,
  () => {
    animationStart.value = 0
  },
)
const { t } = useI18n()
const currentSamples = computed(() => props.progress ?? props.data)
const samples = computed(() =>
  Array.from({ length: props.rounds }, (_, index) => currentSamples.value[index]),
)
const sampleSummary = computed(() =>
  t('networkSamples', {
    success: currentSamples.value.filter((value) => value > 0).length,
    total: currentSamples.value.length,
    rounds: props.rounds,
  }),
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
const sampleHeight = (value: number | undefined) => {
  if (value === undefined || value <= 0) return '5px'
  const px = Math.max(5, Math.min(15, Math.round((value / 330) * 15)))
  return `${px}px`
}
const trackColor = computed(() => {
  if (!props.data.length || !median.value) return 'bg-base-content/15'
  return sampleColor(median.value)
})
const trackWidth = computed(() => {
  if (!props.data.length || !median.value) return '0%'
  return `${Math.max(12, Math.min(100, Math.round((median.value / 360) * 100)))}%`
})
const sampleTip = (value: number | undefined, index: number) =>
  `${props.name} · ${index + 1}/${props.rounds} · ${value === undefined ? t('networkNotTested') : value > 0 ? `${value}ms` : t('networkRequestFailed')}`
</script>

<style scoped>
.network-latency-chart {
  width: 100%;
  min-width: 0;
}
.network-latency-bottom {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.5rem;
  margin-top: 0.5rem;
}
.network-latency-metric {
  display: flex;
  align-items: baseline;
  gap: 2.5px;
  font-variant-numeric: tabular-nums;
}
.network-latency-number {
  font-size: 1.375rem;
  line-height: 1.625rem;
  font-weight: 300;
  letter-spacing: -0.015em;
}
.network-latency-unit {
  font-size: 0.71875rem;
  color: color-mix(in srgb, var(--color-base-content) 60%, transparent);
  font-weight: 400;
}
.network-latency-bars {
  display: flex;
  flex-shrink: 0;
  align-items: flex-end;
  align-self: center;
  gap: 3px;
  height: 15px;
}
.network-latency-bar {
  width: 3px;
  flex-shrink: 0;
  border-radius: 999px;
  transform-origin: center bottom;
}
.network-latency-bar-grow {
  animation: network-latency-grow 250ms cubic-bezier(0.2, 0.7, 0.3, 1);
}
@keyframes network-latency-grow {
  from {
    transform: scaleY(0);
  }
  to {
    transform: scaleY(1);
  }
}
@media (prefers-reduced-motion: reduce) {
  .network-latency-bar-grow {
    animation: none;
  }
}
</style>
