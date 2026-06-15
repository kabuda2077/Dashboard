<template>
  <div class="relative flex flex-col text-sm">
    <div class="mt-1 mb-3 flex items-center gap-2 px-1 text-lg font-semibold">
      <div class="indicator">
        <h2>
          {{ $t('zashboardSettings') }}
        </h2>
      </div>
    </div>

    <DashboardSettings />
    <StyleSettings />
    <GeneralSettings />
  </div>
</template>

<script setup lang="ts">
import DashboardSettings from '../../common/DashboardSettings.vue'
import GeneralSettings from './GeneralSettings.vue'
import StyleSettings from './StyleSettings.vue'
</script>
