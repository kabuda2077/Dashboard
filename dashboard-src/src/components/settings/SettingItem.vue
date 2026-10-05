<template>
  <div
    v-if="when"
    class="setting-item"
  >
    <slot />
  </div>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    /** Additional prerequisite for dependent settings. */
    when?: boolean
  }>(),
  { when: true },
)
</script>
