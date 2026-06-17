<template>
  <details
    ref="detailsRef"
    class="dropdown dropdown-bottom"
  >
    <summary
      class="border-base-content/20 bg-base-100 flex h-9 min-h-9 cursor-pointer list-none items-center justify-between gap-3 rounded-lg border px-4 text-sm shadow-none outline-none select-none focus:outline-none"
    >
      <span class="truncate">{{ selectedLabel }}</span>
      <ChevronDownIcon class="h-4 w-4 shrink-0" />
    </summary>
    <ul
      class="dropdown-content border-base-content/20 bg-base-100 menu overflow-hidden rounded-lg border p-1 shadow-xs"
      :class="menuClass"
    >
      <li
        v-for="option in options"
        :key="option.key ?? String(option.value)"
      >
        <button
          class="grid min-w-0 grid-cols-[1rem_minmax(0,1fr)] items-center gap-3 rounded-lg px-3 py-1.5 text-left text-sm leading-5"
          type="button"
          @click="selectOption(option.value)"
        >
          <CheckIcon
            v-if="isSelected(option.value)"
            class="h-4 w-4"
          />
          <span
            v-else
            class="h-4 w-4"
          />
          <span class="min-w-0 truncate">{{ option.label }}</span>
        </button>
      </li>
    </ul>
  </details>
</template>

<script setup lang="ts">
import { CheckIcon, ChevronDownIcon } from '@heroicons/vue/24/outline'
import { computed, ref } from 'vue'

type DropdownOption = {
  label: string
  key?: string
  value: unknown
}

const model = defineModel<unknown>({
  required: true,
})

const props = withDefaults(
  defineProps<{
    menuClass?: string
    options: DropdownOption[]
  }>(),
  {
    menuClass: 'mt-2 w-full min-w-full',
  },
)

const detailsRef = ref<HTMLDetailsElement | null>(null)

const selectedLabel = computed(() => {
  return props.options.find((option) => option.value === model.value)?.label ?? model.value
})

const isSelected = (value: unknown) => {
  return Object.is(value, model.value)
}

const selectOption = (value: unknown) => {
  model.value = value
  if (detailsRef.value) {
    detailsRef.value.open = false
  }
}
</script>
