<template>
  <div class="bg-base-200/30 flex flex-col rounded-xl p-4">
    <div class="flex items-center justify-between">
      <div class="text-base-content/60 text-xs font-semibold tracking-wider uppercase">
        {{ $t('latency') }}
      </div>
      <button
        class="btn btn-ghost btn-xs btn-circle"
        :disabled="isTesting"
        @click="getLatency"
      >
        <BoltIcon
          class="h-3.5 w-3.5"
          :class="isTesting ? 'animate-pulse' : ''"
        />
      </button>
    </div>

    <div class="mt-2 grid grid-cols-1 gap-4 sm:grid-cols-2">
      <div
        v-for="item in latencyItems"
        :key="item.name"
        class="flex min-w-0 flex-col gap-0.5"
      >
        <div class="flex min-w-0 items-center gap-1.5">
          <span class="text-base-content/70 inline-block w-16 shrink-0 truncate text-xs">
            {{ item.name }}
          </span>
          <LatencyChart
            :data="item.values"
            :name="item.name"
            :rounds="ROUNDS"
            class="min-w-0 flex-1"
          />
        </div>
        <div class="flex flex-wrap gap-x-4 text-[11px] tabular-nums">
          <template v-if="item.stats">
            <span
              v-for="stat in item.stats"
              :key="stat.label"
            >
              <span class="text-base-content/40 mr-1">{{ stat.label }}</span>
              <span
                v-if="stat.value"
                :class="getColorForLatency(stat.value)"
              >
                {{ stat.value }}ms
              </span>
              <span
                v-else
                class="text-base-content/30"
              >
                --
              </span>
            </span>
          </template>
          <span
            v-else
            class="text-base-content/30"
          >
            --
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import {
  getLatency,
  isTestingLatency as isTesting,
  LATENCY_ROUNDS as ROUNDS,
  latencyTargets as targets,
} from '@/composables/overview'
import { getColorForLatency } from '@/helper'
import { autoConnectionCheck } from '@/store/settings'
import { BoltIcon } from '@heroicons/vue/24/outline'
import { computed, onMounted } from 'vue'
import LatencyChart from './LatencyChart.vue'

const computeStats = (values: number[]) => {
  const ok = values.filter((v) => v > 0).sort((a, b) => a - b)
  if (!ok.length) return null

  return [
    { label: 'min', value: ok[0] },
    { label: 'max', value: ok[ok.length - 1] },
  ]
}

const latencyItems = computed(() =>
  targets.map((target) => ({
    name: target.name,
    values: target.ref.value,
    stats: computeStats(target.ref.value),
  })),
)

onMounted(() => {
  if (autoConnectionCheck.value && targets.every((target) => target.ref.value.length === 0)) {
    getLatency()
  }
})
</script>
