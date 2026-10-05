<template>
  <!-- overview -->
  <div class="mb-3 flex flex-col gap-3">
    <div class="relative">
      <ChartsCard class="shadow-none" />
    </div>
    <div class="relative">
      <NetworkCard class="shadow-none" />
    </div>
  </div>
</template>

<script setup lang="ts">
import ChartsCard from '@/components/overview/ChartsCard.vue'
import NetworkCard from '@/components/overview/NetworkCard.vue'
</script>
