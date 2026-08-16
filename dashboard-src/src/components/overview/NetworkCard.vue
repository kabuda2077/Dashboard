<template>
  <div class="network-card base-container w-full p-4">
    <div class="network-card-grid grid grid-cols-1 gap-3">
      <ConnectionStatus class="network-card-status" />
      <IPCheck />
    </div>
  </div>
</template>

<script setup lang="ts">
import ConnectionStatus from '@/components/overview/ConnectionStatus.vue'
import IPCheck from '@/components/overview/IPCheck.vue'
</script>

<style scoped>
.network-card {
  container-type: inline-size;
}

@container (min-width: 768px) {
  .network-card-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }

  .network-card-status {
    grid-column: span 2 / span 2;
  }
}
</style>
