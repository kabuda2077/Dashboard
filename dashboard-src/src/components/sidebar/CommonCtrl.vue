<template>
  <div class="flex flex-col gap-3 p-2.5 text-sm">
    <SidebarStatistics />
    <div class="flex gap-2">
      <SidebarButtons />
    </div>
  </div>
</template>

<script setup lang="ts">
import SidebarButtons from './SidebarButtons.vue'
import SidebarStatistics from './SidebarStatistics.vue'
</script>
