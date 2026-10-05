<template>
  <div
    class="pointer-events-none top-0 z-30"
    :class="pageAligned ? 'bg-base-200 sticky w-full' : 'fixed right-0 bg-transparent p-3 pb-0'"
    :style="ctrlsBarStyle"
    ref="ctrlsBarRef"
  >
    <div
      class="flex items-start gap-3"
      :class="[
        pageAligned && 'core-controls-container mx-auto w-full max-w-7xl p-3 pb-0',
        showWindowControls ? 'justify-between' : '',
        rowClass,
      ]"
    >
      <div
        class="ctrls-bar pointer-events-auto relative min-w-0 overflow-visible!"
        :class="[
          pageAligned || !showWindowControls ? 'w-full' : 'w-fit',
          solid && 'ctrls-bar-solid',
        ]"
        :style="ctrlsBarContentStyle"
      >
        <slot></slot>
      </div>
      <WindowControls
        v-if="showWindowControls"
        class="pointer-events-auto shrink-0"
        :class="pageAligned && 'fixed top-3 right-3'"
      />
    </div>
  </div>
</template>
<script lang="ts" setup>
import WindowControls from '@/components/common/WindowControls.vue'
import { hasHostBridge } from '@/composables/hostBridge'
import { ctrlsBottom } from '@/composables/paddingViews'
import { isMiddleScreen } from '@/helper/utils'
import { useElementBounding } from '@vueuse/core'
import { computed, onUnmounted, ref, watch } from 'vue'

const ctrlsBarRef = ref<HTMLDivElement | null>(null)
const props = defineProps<{
  rowClass?: string
  solid?: boolean
  pageAligned?: boolean
}>()
const { bottom: ctrlsBarBottom } = useElementBounding(ctrlsBarRef)
const showWindowControls = computed(
  () => !isMiddleScreen.value && (hasHostBridge || import.meta.env.DEV),
)
const ctrlsBarStyle = computed(() =>
  props.pageAligned
    ? undefined
    : {
        left: 'var(--sidebar-width, 0px)',
        transition: isMiddleScreen.value ? undefined : 'left 320ms cubic-bezier(0.34,0.1,0.2,1)',
      },
)
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
