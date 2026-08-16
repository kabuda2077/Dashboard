<template>
  <div>
    <div class="mt-1 mb-3 px-1 text-lg font-semibold">关于</div>
    <div class="settings-grid">
      <div class="setting-item">
        <div class="setting-item-label">当前版本</div>
        <div class="text-base-content/65 text-sm">{{ currentAppVersion }}</div>
      </div>
      <div class="setting-item">
        <div class="setting-item-label flex min-w-0 items-center gap-2">
          <span>应用更新</span>
          <span
            v-if="hostState.isAppUpdateChecking"
            class="text-base-content/55 truncate text-xs font-normal"
          >
            正在检查...
          </span>
          <span
            v-else-if="updateFeedback"
            class="text-base-content/55 truncate text-xs font-normal"
          >
            {{ updateFeedback }}
          </span>
          <span
            v-else-if="hostState.appUpdateAvailable && latestAppVersion"
            class="text-base-content/55 truncate text-xs font-normal"
          >
            发现 {{ latestAppVersion }}
          </span>
        </div>
        <div class="flex shrink-0 items-center gap-2">
          <button
            class="btn btn-square btn-sm dashboard-action-btn"
            :disabled="hostState.isAppUpdateChecking"
            title="检查 Dashboard 更新"
            @click="checkAppUpdate"
          >
            <ArrowPathIcon
              class="h-4 w-4"
              :class="hostState.isAppUpdateChecking ? 'animate-spin' : ''"
            />
          </button>
          <button
            class="btn btn-sm"
            :class="
              hostState.appUpdateAvailable ? 'btn-primary' : 'btn-square dashboard-action-btn'
            "
            title="打开 GitHub Release"
            @click="postHostMessage({ type: 'openAppRelease' })"
          >
            <ArrowTopRightOnSquareIcon class="h-4 w-4" />
            <span v-if="hostState.appUpdateAvailable">下载更新</span>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import {
  addHostMessageListener,
  hostState,
  postHostMessage,
  type HostMessage,
} from '@/composables/hostBridge'
import { ArrowPathIcon, ArrowTopRightOnSquareIcon } from '@heroicons/vue/24/outline'
import { computed, onMounted, onUnmounted, ref } from 'vue'

const currentAppVersion = computed(() =>
  hostState.value.appVersion ? `v${hostState.value.appVersion}` : '未知版本',
)
const latestAppVersion = computed(() =>
  hostState.value.latestAppVersion ? `v${hostState.value.latestAppVersion}` : '',
)
const updateFeedback = ref('')
let clearFeedbackTimer: ReturnType<typeof setTimeout> | undefined
let removeHostMessageListener: (() => void) | undefined

const showUpdateFeedback = (message: string) => {
  window.clearTimeout(clearFeedbackTimer)
  updateFeedback.value = message
  clearFeedbackTimer = window.setTimeout(() => {
    updateFeedback.value = ''
  }, 3000)
}

const handleHostMessage = (event: MessageEvent<HostMessage>) => {
  const message = event.data?.message ?? ''
  if (event.data?.type !== 'notice') {
    return
  }
  if (message.startsWith('当前已是最新版本')) {
    showUpdateFeedback('已是最新版本')
  } else if (message.startsWith('发现 Dashboard 新版本')) {
    const version = message.match(/v([^，]+)/)?.[1]
    showUpdateFeedback(version ? `发现 v${version}` : '发现新版本')
  }
}

const checkAppUpdate = () => {
  window.clearTimeout(clearFeedbackTimer)
  updateFeedback.value = ''
  postHostMessage({ type: 'checkAppUpdate' })
}

onMounted(() => {
  removeHostMessageListener = addHostMessageListener(handleHostMessage)
})

onUnmounted(() => {
  removeHostMessageListener?.()
  window.clearTimeout(clearFeedbackTimer)
})
</script>
