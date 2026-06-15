<template>
  <!-- overview -->
  <div class="mb-3 flex flex-col gap-3">
    <ChartsCard class="shadow-none" />
    <NetworkCard class="shadow-none" />
  </div>
</template>

<script setup lang="ts">
import ChartsCard from '@/components/overview/ChartsCard.vue'
import NetworkCard from '@/components/overview/NetworkCard.vue'
</script>
