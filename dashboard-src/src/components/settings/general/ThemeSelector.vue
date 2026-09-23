<template>
  <SelectInput
    v-model="theme"
    class="select select-sm w-48"
    :options="themeOptions"
    panel-class="p-0!"
    option-class="rounded-none!"
  >
    <template #option="{ option }">
      <span class="flex items-center gap-2">
        <span class="bg-primary rounded-field h-3 w-5 flex-none shadow"></span>
        <span class="truncate">{{ option.label }}</span>
      </span>
    </template>
  </SelectInput>
</template>

<script setup lang="ts">
import SelectInput from '@/components/common/SelectInput.vue'
import { ALL_THEME } from '@/constant'
import { customThemes } from '@/store/settings'
import { computed } from 'vue'

const theme = defineModel<string>('value', {
  type: String,
  required: true,
})

const themes = computed(() => {
  if (customThemes.value.length) {
    return [...ALL_THEME, ...customThemes.value.map((theme) => theme.name)]
  }

  return ALL_THEME
})
const themeOptions = computed(() =>
  themes.value.map((value) => ({
    value,
    label: value,
    theme: value,
  })),
)
</script>
