<template>
  <div class="base-container grid grid-cols-1 gap-3 p-4 lg:grid-cols-3">
    <ConnectionStatus class="lg:col-span-2" />
    <IPCheck />
  </div>
</template>

<script setup lang="ts">
import ConnectionStatus from '@/components/overview/ConnectionStatus.vue'
import IPCheck from '@/components/overview/IPCheck.vue'
</script>
