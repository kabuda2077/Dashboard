<template>
  <!-- overview -->
  <div class="mb-3 flex flex-col gap-3">
    <div
      v-if="isVisibleOverviewCard"
      class="relative"
    >
      <ChartsCard class="shadow-none" />
    </div>
    <div
      v-if="isVisibleNetworkCard"
      class="relative"
    >
      <NetworkCard class="shadow-none" />
    </div>
  </div>
</template>

<script setup lang="ts">
import ChartsCard from '@/components/overview/ChartsCard.vue'
import NetworkCard from '@/components/overview/NetworkCard.vue'
import { useIsSettingVisible } from '@/composables/settings'
import { OVERVIEW_ITEM_KEYS } from '@/config/settingsItems'

const isVisibleOverviewCard = useIsSettingVisible(OVERVIEW_ITEM_KEYS.chartsCard)
const isVisibleNetworkCard = useIsSettingVisible(OVERVIEW_ITEM_KEYS.networkCard)
</script>
