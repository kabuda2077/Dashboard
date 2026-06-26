<template>
  <div class="bg-base-200/30 flex flex-col rounded-xl p-4">
    <div class="flex items-center justify-between">
      <div class="text-base-content/60 text-xs font-semibold tracking-wider uppercase">
        {{ $t('latency') }}
      </div>
      <button
        class="btn btn-ghost btn-xs btn-circle"
        @click="getLatency"
      >
        <BoltIcon class="h-3.5 w-3.5" />
      </button>
    </div>

    <div class="mt-3 flex flex-col gap-2.5">
      <div
        v-for="item in latencyItems"
        :key="item.name"
        class="flex items-center justify-between"
      >
        <span class="text-base-content/70 text-sm">{{ item.name }}</span>
        <span
          class="flex items-center gap-1.5 text-sm font-medium"
          :class="item.value ? getColorForLatency(Number(item.value)) : 'text-base-content/20'"
        >
          <template v-if="item.value">{{ item.value }}ms</template>
          <template v-else>--</template>
          <SignalStrength
            v-if="item.value"
            :latency="Number(item.value)"
          />
        </span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import {
  getBaiduLatencyAPI,
  getCloudflareLatencyAPI,
  getGithubLatencyAPI,
  getYouTubeLatencyAPI,
} from '@/api/latency'
import {
  baiduLatency,
  cloudflareLatency,
  githubLatency,
  youtubeLatency,
} from '@/composables/overview'
import { getColorForLatency } from '@/helper'
import { autoConnectionCheck } from '@/store/settings'
import { BoltIcon } from '@heroicons/vue/24/outline'
import { computed, onMounted } from 'vue'
import SignalStrength from './SignalStrength.vue'

const latencyItems = computed(() => [
  { name: 'Baidu', value: baiduLatency.value },
  { name: 'Cloudflare', value: cloudflareLatency.value },
  { name: 'GitHub', value: githubLatency.value },
  { name: 'YouTube', value: youtubeLatency.value },
])

const getLatency = async () => {
  getBaiduLatencyAPI().then((res) => {
    baiduLatency.value = res.toFixed(0)
  })

  getCloudflareLatencyAPI().then((res) => {
    cloudflareLatency.value = res.toFixed(0)
  })

  getGithubLatencyAPI().then((res) => {
    githubLatency.value = res.toFixed(0)
  })

  getYouTubeLatencyAPI().then((res) => {
    youtubeLatency.value = res.toFixed(0)
  })
}

onMounted(() => {
  if (
    autoConnectionCheck.value &&
    [baiduLatency, cloudflareLatency, githubLatency, youtubeLatency].some(
      (item) => item.value === '',
    )
  ) {
    getLatency()
  }
})
</script>
