<template>
  <section class="network-test-page">
    <div
      v-if="!hideHeader"
      class="flex items-center justify-between"
    >
      <div
        class="text-base-content/60 text-xs font-semibold tracking-wider uppercase"
        :title="$t('networkLatencyHelp')"
      >
        {{ $t('latency') }}
      </div>
      <button
        class="network-icon-button"
        type="button"
        :disabled="targets.every((target) => target.checking.value) || !backendSessionReady"
        :aria-label="$t('networkTestLatency')"
        @click="getLatency()"
      >
        <BoltIcon
          class="h-3.5 w-3.5"
          :class="isTesting ? 'animate-pulse' : ''"
        />
      </button>
    </div>
    <div class="network-test-grid network-latency-grid">
      <button
        v-for="target in targets"
        :key="target.id"
        :data-network-target="target.id"
        type="button"
        class="network-bento-tile"
        :disabled="target.checking.value || !backendSessionReady"
        :aria-label="$t('networkTestTarget', { name: $t(`networkTarget_${target.id}`) })"
        :aria-busy="target.checking.value"
        @click="getLatency([target.id])"
      >
        <div class="network-tile-top">
          <div class="network-tile-title-wrap">
            <span
              class="network-tile-dot"
              :class="dotColor(target.ref.value)"
            />
            <span
              class="network-tile-name"
              :title="target.url"
            >
              {{ $t(`networkTarget_${target.id}`) }}
            </span>
          </div>
          <span class="network-tile-badge">
            {{ $t(target.region === 'domestic' ? 'networkDomestic' : 'networkInternational') }}
          </span>
        </div>
        <LatencyChart
          :data="target.ref.value"
          :progress="target.progress.value"
          :attempt="target.attempt.value"
          :rounds="ROUNDS"
          :name="target.name"
        />
      </button>
    </div>
  </section>
</template>

<script setup lang="ts">
import {
  getLatency,
  isTestingLatency as isTesting,
  LATENCY_ROUNDS as ROUNDS,
  latencyTargets as targets,
} from '@/helper/overview'
import { backendSessionReady } from '@/helper/backendSession'
import { useNetworkDiagnosticsPage } from '@/helper/networkDiagnostics'
import { BoltIcon } from '@heroicons/vue/24/outline'
import { toRef } from 'vue'
import LatencyChart from './LatencyChart.vue'
import { getColorForLatency } from '@/helper'

const props = withDefaults(defineProps<{ active?: boolean; hideHeader?: boolean }>(), {
  active: true,
  hideHeader: false,
})
useNetworkDiagnosticsPage('latency', toRef(props, 'active'))

const median = (samples: number[]) => {
  const successes = samples.filter((v) => v > 0)
  if (!successes.length) return 0
  const sorted = [...successes].sort((a, b) => a - b)
  const middle = Math.floor(sorted.length / 2)
  return Math.round(sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2)
}
const dotColor = (samples: number[]) => {
  const m = median(samples)
  if (m <= 0) return 'bg-base-content/20'
  const color = getColorForLatency(m)
  return color.replace('text-', 'bg-')
}
</script>
