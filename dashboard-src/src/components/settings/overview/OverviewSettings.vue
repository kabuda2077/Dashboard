<template>
  <!-- overview -->
  <template v-if="!splitOverviewPage">
    <OverviewCard />
  </template>

  <div class="flex flex-col gap-3 text-sm">
    <div class="settings-grid">
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('splitOverviewPage') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="splitOverviewPage"
        />
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('autoIPCheckWhenStart') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="autoIPCheck"
        />
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('autoConnectionCheckWhenStart') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="autoConnectionCheck"
        />
      </div>
      <div class="setting-item max-md:hidden">
        <div class="setting-item-label">
          {{ $t('showStatisticsWhenSidebarCollapsed') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="showStatisticsWhenSidebarCollapsed"
        />
      </div>
      <div class="setting-item max-md:hidden">
        <div class="setting-item-label">
          {{ $t('numberOfChartsInSidebar') }}
        </div>
        <select
          class="select select-sm min-w-24"
          v-model="numberOfChartsInSidebar"
        >
          <option
            v-for="opt in [1, 2, 3]"
            :key="opt"
            :value="opt"
          >
            {{ opt }}
          </option>
        </select>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import {
  autoConnectionCheck,
  autoIPCheck,
  numberOfChartsInSidebar,
  showStatisticsWhenSidebarCollapsed,
  splitOverviewPage,
} from '@/store/settings'
import OverviewCard from './OverviewCard.vue'
</script>
