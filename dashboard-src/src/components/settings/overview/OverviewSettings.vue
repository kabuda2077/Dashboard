<template>
  <!-- overview -->
  <template v-if="!splitOverviewPage">
    <OverviewCard />
  </template>

  <div class="flex flex-col gap-3 text-sm">
    <div class="settings-grid">
      <SettingItem>
        <div class="setting-item-label">
          {{ $t('splitOverviewPage') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="splitOverviewPage"
        />
      </SettingItem>
      <SettingItem>
        <div class="setting-item-label">
          {{ $t('autoIPCheckWhenStart') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="autoIPCheck"
        />
      </SettingItem>
      <SettingItem>
        <div class="setting-item-label">
          {{ $t('autoConnectionCheckWhenStart') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="autoConnectionCheck"
        />
      </SettingItem>
      <SettingItem class="max-md:hidden">
        <div class="setting-item-label">
          {{ $t('showStatisticsWhenSidebarCollapsed') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="showStatisticsWhenSidebarCollapsed"
        />
      </SettingItem>
      <SettingItem class="max-md:hidden">
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
      </SettingItem>
    </div>
  </div>
</template>

<script setup lang="ts">
import SettingItem from '@/components/settings/SettingItem.vue'
import { defineAsyncComponent } from 'vue'
import {
  autoConnectionCheck,
  autoIPCheck,
  numberOfChartsInSidebar,
  showStatisticsWhenSidebarCollapsed,
  splitOverviewPage,
} from '@/store/settings'
const OverviewCard = defineAsyncComponent(() => import('./OverviewCard.vue'))
</script>
