<template>
  <div class="flex flex-col gap-2 p-2 text-sm">
    <SidebarStatistics />
    <BackendSwitch :disable-edit-backend="true" />
    <div class="flex gap-2">
      <SidebarButtons />
      <BackendVersion />
    </div>
  </div>
</template>

<script setup lang="ts">
import BackendVersion from '../common/BackendVersion.vue'
import BackendSwitch from '../settings/backend/BackendSwitch.vue'
import SidebarButtons from './SidebarButtons.vue'
import SidebarStatistics from './SidebarStatistics.vue'
</script>
