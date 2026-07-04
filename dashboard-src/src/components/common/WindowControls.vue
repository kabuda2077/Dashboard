<template>
  <div
    v-if="showWindowControls"
    class="flex select-none items-center gap-2"
  >
    <button
      class="btn btn-circle bg-base-100 hover:bg-base-200 h-9 min-h-9 w-9 p-0 shadow-xs transition-colors active:scale-95"
      aria-label="最小化"
      title="最小化"
      @click="post('windowMinimize')"
    >
      <MinusIcon class="h-4 w-4" />
    </button>
    <button
      class="btn btn-circle bg-base-100 hover:bg-base-200 h-9 min-h-9 w-9 p-0 shadow-xs transition-colors active:scale-95"
      :aria-label="isMaximized ? '还原' : '最大化'"
      :title="isMaximized ? '还原' : '最大化'"
      @click="post('windowToggleMaximize')"
    >
      <component
        :is="isMaximized ? Square2StackIcon : StopIcon"
        class="h-4 w-4"
      />
    </button>
    <button
      class="btn btn-circle bg-base-100 hover:bg-error hover:text-error-content h-9 min-h-9 w-9 p-0 shadow-xs transition-colors active:scale-95"
      aria-label="关闭"
      title="关闭"
      @click="post('windowClose')"
    >
      <XMarkIcon class="h-4 w-4" />
    </button>
  </div>
</template>

<script setup lang="ts">
import {
  addHostMessageListener,
  applyHostMessage,
  hasHostBridge,
  hostWindowMaximized,
  postHostMessage,
  type HostMessage,
} from '@/composables/hostBridge'
import { MinusIcon, Square2StackIcon, StopIcon, XMarkIcon } from '@heroicons/vue/24/outline'
import { onMounted, onUnmounted } from 'vue'

const showWindowControls = hasHostBridge || import.meta.env.DEV
const isMaximized = hostWindowMaximized
let removeHostMessageListener: (() => void) | undefined

const post = (type: string) => {
  postHostMessage({ type })
}

const handleHostMessage = (event: MessageEvent<HostMessage>) => {
  applyHostMessage(event.data)
}

onMounted(() => {
  removeHostMessageListener = addHostMessageListener(handleHostMessage)
  post('requestWindowState')
})

onUnmounted(() => {
  removeHostMessageListener?.()
  removeHostMessageListener = undefined
})
</script>
