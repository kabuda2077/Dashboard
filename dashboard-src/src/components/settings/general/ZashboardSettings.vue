<template>
  <div class="relative flex flex-col text-sm">
    <div class="flex items-center gap-2 px-1">
      <div class="text-lg font-semibold">
        Dashboard
      </div>
    </div>

    <StyleSettings />
    <GeneralSettings />
  </div>
</template>

<script setup lang="ts">
import GeneralSettings from './GeneralSettings.vue'
import StyleSettings from './StyleSettings.vue'
</script>
