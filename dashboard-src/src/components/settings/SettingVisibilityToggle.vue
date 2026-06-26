<template>
  <button
    v-if="settingsEditMode"
    type="button"
    class="vis-toggle group relative h-5 w-5 shrink-0 cursor-pointer rounded-full transition-[background-color,color,transform] duration-300 ease-out active:scale-90"
    :class="isHidden ? 'text-success' : 'text-error'"
    :data-hidden="isHidden"
    :title="$t('settingsVisibility')"
    @click.stop.prevent="handleToggle"
  >
    <Transition
      name="sign-spin"
      mode="out-in"
    >
      <PlusCircleIcon
        v-if="isHidden"
        key="plus"
        class="h-5 w-5 drop-shadow-sm transition-transform duration-200 group-hover:scale-110"
      />
      <MinusCircleIcon
        v-else
        key="minus"
        class="h-5 w-5 drop-shadow-sm transition-transform duration-200 group-hover:scale-110"
      />
    </Transition>
  </button>
</template>

<script setup lang="ts">
import { isSettingHidden, settingsEditMode, toggleSettingHidden } from '@/composables/settings'
import { MinusCircleIcon, PlusCircleIcon } from '@heroicons/vue/24/solid'
import { computed, ref } from 'vue'

const props = defineProps<{
  settingKey: string
}>()

const isHidden = computed(() => isSettingHidden(props.settingKey))

const isToggling = ref(false)
const handleToggle = () => {
  if (isToggling.value) return
  isToggling.value = true
  toggleSettingHidden(props.settingKey)
  window.setTimeout(() => {
    isToggling.value = false
  }, 180)
}
</script>

<style scoped>
.vis-toggle {
  background-color: color-mix(in srgb, currentColor 12%, transparent);
}

.sign-spin-enter-active,
.sign-spin-leave-active {
  transition: transform 220ms cubic-bezier(0.34, 1.56, 0.64, 1);
}

.sign-spin-leave-active {
  position: absolute;
  inset: 0;
}

.sign-spin-enter-from {
  transform: rotate(-180deg);
}

.sign-spin-leave-to {
  transform: rotate(180deg);
}

.sign-spin-enter-to,
.sign-spin-leave-from {
  transform: rotate(0deg);
}
</style>
