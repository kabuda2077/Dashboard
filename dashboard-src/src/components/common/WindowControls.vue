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
import { MinusIcon, Square2StackIcon, StopIcon, XMarkIcon } from '@heroicons/vue/24/outline'
import { onMounted, onUnmounted, ref } from 'vue'

type HostMessage = {
  type?: string
  isMaximized?: boolean
  state?: {
    isWindowMaximized?: boolean
  }
}

type HostWindow = Window & {
  chrome?: {
    webview?: {
      postMessage?: (message: unknown) => void
      addEventListener?: (
        type: 'message',
        listener: (event: MessageEvent<HostMessage>) => void,
      ) => void
      removeEventListener?: (
        type: 'message',
        listener: (event: MessageEvent<HostMessage>) => void,
      ) => void
    }
  }
}

const hostWindow = window as HostWindow
const hasHostWindowControls = Boolean(hostWindow.chrome?.webview?.postMessage)
const showWindowControls = hasHostWindowControls || import.meta.env.DEV
const isMaximized = ref(false)

const post = (type: string) => {
  hostWindow.chrome?.webview?.postMessage?.({ type })
}

const handleHostMessage = (event: MessageEvent<HostMessage>) => {
  if (event.data?.type === 'windowState') {
    isMaximized.value = !!event.data.isMaximized
    return
  }

  if (event.data?.type === 'state') {
    isMaximized.value = !!event.data.state?.isWindowMaximized
  }
}

onMounted(() => {
  hostWindow.chrome?.webview?.addEventListener?.('message', handleHostMessage)
  post('requestWindowState')
})

onUnmounted(() => {
  hostWindow.chrome?.webview?.removeEventListener?.('message', handleHostMessage)
})
</script>
