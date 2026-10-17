<template>
  <section class="network-test-page">
    <div class="flex items-center justify-between">
      <div
        class="text-base-content/60 text-xs font-semibold tracking-wider uppercase"
        :title="$t('networkLatencyHelp')"
      >
        {{ $t('latency') }}
      </div>
      <button
        class="btn btn-ghost btn-xs btn-circle"
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
      <div
        v-for="target in targets"
        :key="target.id"
        class="flex min-w-0 flex-col justify-center gap-2"
        :data-network-target="target.id"
      >
        <button
          type="button"
          class="network-target-button"
          :disabled="target.checking.value || !backendSessionReady"
          :aria-label="$t('networkTestTarget', { name: $t(`networkTarget_${target.id}`) })"
          :aria-busy="target.checking.value"
          @click="getLatency([target.id])"
        >
          <div class="flex min-w-0 items-center gap-2">
            <span
              class="min-w-0 truncate text-sm font-medium"
              :title="target.url"
            >
              {{ $t(`networkTarget_${target.id}`) }}
            </span>
            <span
              v-if="target.checking.value"
              class="loading loading-spinner h-3 w-3 shrink-0"
              aria-hidden="true"
            />
            <span class="text-base-content/45 shrink-0 text-[10px]">
              {{ $t(target.region === 'domestic' ? 'networkDomestic' : 'networkInternational') }}
            </span>
          </div>
          <div class="flex min-w-0 items-center gap-2">
            <LatencyChart
              :data="target.ref.value"
              :rounds="ROUNDS"
              :name="target.name"
            />
          </div>
        </button>
      </div>
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

const props = withDefaults(defineProps<{ active?: boolean }>(), { active: true })
useNetworkDiagnosticsPage('latency', toRef(props, 'active'))
</script>
