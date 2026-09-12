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
    /** Stable key retained for settings metadata and search compatibility. */
    settingKey: string
    /** Additional prerequisite for dependent settings. */
    when?: boolean
  }>(),
  { when: true },
)
</script>
