<template>
  <div
    class="grid min-w-0 gap-2"
    :style="`grid-template-columns: repeat(auto-fill, minmax(min(${minProxyCardWidth}px, 100%), 1fr));`"
  >
    <TransitionGroup name="proxy-node">
      <slot />
    </TransitionGroup>
  </div>
</template>

<script lang="ts" setup>
import { minProxyCardWidth } from '@/store/settings'
</script>
