<template>
  <div class="flex flex-col gap-2 p-2 text-sm">
    <SidebarStatistics />
    <BackendSwitch v-if="!hasHostBridge" :disable-edit-backend="true" />
    <div class="flex gap-2">
      <SidebarButtons />
    </div>
  </div>
</template>

<script setup lang="ts">
import { hasHostBridge } from '@/composables/hostBridge'
import BackendSwitch from '../settings/backend/BackendSwitch.vue'
import SidebarButtons from './SidebarButtons.vue'
import SidebarStatistics from './SidebarStatistics.vue'
</script>
