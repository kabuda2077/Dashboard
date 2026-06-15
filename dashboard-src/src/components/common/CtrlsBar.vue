<template>
  <div
    class="pointer-events-none fixed top-0 right-0 z-30 bg-transparent p-3 pb-0"
    :style="ctrlsBarStyle"
    ref="ctrlsBarRef"
  >
    <div
      class="flex items-start gap-3"
      :class="[showWindowControls ? 'justify-between' : '', rowClass]"
    >
      <div
        class="ctrls-bar pointer-events-auto relative min-w-0 overflow-visible!"
        :class="showWindowControls ? 'w-fit' : 'w-full'"
        :style="ctrlsBarContentStyle"
      >
        <slot></slot>
      </div>
      <WindowControls
        v-if="showWindowControls"
        class="pointer-events-auto shrink-0"
      />
    </div>
  </div>
</template>
<script lang="ts" setup>
import WindowControls from '@/components/common/WindowControls.vue'
import { ctrlsBottom } from '@/composables/paddingViews'
import { isMiddleScreen } from '@/helper/utils'
import { isSidebarCollapsed } from '@/store/settings'
import { useElementBounding } from '@vueuse/core'
import { computed, onUnmounted, ref, watch } from 'vue'

type HostWindow = Window & {
  chrome?: {
    webview?: {
      postMessage?: (message: unknown) => void
    }
  }
}

const ctrlsBarRef = ref<HTMLDivElement | null>(null)
defineProps<{
  rowClass?: string
}>()
const { bottom: ctrlsBarBottom } = useElementBounding(ctrlsBarRef)
const hasHostWindowControls = Boolean((window as HostWindow).chrome?.webview?.postMessage)
const showWindowControls = computed(
  () => !isMiddleScreen.value && (hasHostWindowControls || import.meta.env.DEV),
)
const ctrlsBarStyle = computed(() => ({
  left: isMiddleScreen.value ? '0' : isSidebarCollapsed.value ? '4.5rem' : '16rem',
  transition: isMiddleScreen.value ? undefined : 'left 320ms cubic-bezier(0.34,0.1,0.2,1)',
}))
const ctrlsBarContentStyle = computed(() =>
  showWindowControls.value
    ? {
        maxWidth: 'calc(100% - 11rem)',
      }
    : undefined,
)

watch(
  ctrlsBarBottom,
  () => {
    ctrlsBottom.value = ctrlsBarBottom.value
  },
  { immediate: true },
)

onUnmounted(() => {
  ctrlsBottom.value = 0
})
</script>
