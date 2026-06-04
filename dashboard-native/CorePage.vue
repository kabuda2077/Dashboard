<template>
  <div class="h-full overflow-x-hidden overflow-y-auto p-3">
    <div class="mx-auto flex max-w-5xl flex-col gap-3">
      <section class="base-container flex flex-col gap-4 p-4 md:flex-row md:items-center md:justify-between">
        <div class="flex items-center gap-3">
          <span
            class="h-3 w-3 rounded-full"
            :class="runtime.isRunning ? 'bg-success shadow-success/30 shadow-[0_0_0_4px]' : 'bg-warning shadow-warning/30 shadow-[0_0_0_4px]'"
          />
          <div>
            <h1 class="text-2xl font-semibold">Mihomo Core</h1>
            <p class="text-base-content/60 mt-1 text-sm">
              {{ runtime.isRunning ? `运行中 / PID ${runtime.processId ?? ''}` : '未运行' }}
            </p>
          </div>
        </div>

        <div class="flex flex-wrap gap-2">
          <button
            class="btn btn-primary btn-sm"
            :disabled="runtime.isRunning || runtime.isCoreUpgrading"
            @click="startCore"
          >
            启动内核
          </button>
          <button
            class="btn btn-sm"
            :disabled="!runtime.isRunning || runtime.isCoreUpgrading"
            @click="post({ ...collect(), type: 'restart' })"
          >
            重启内核
          </button>
          <button
            class="btn btn-warning btn-sm"
            :disabled="!runtime.isRunning || runtime.isCoreUpgrading"
            @click="post({ type: 'stop' })"
          >
            停止内核
          </button>
          <button
            class="btn btn-secondary btn-sm"
            :disabled="runtime.isCoreUpgrading"
            @click="post({ ...collect(), type: 'upgradeCore' })"
          >
            <span
              v-if="runtime.isCoreUpgrading"
              class="loading loading-spinner loading-xs"
            />
            {{ runtime.isCoreUpgrading ? '升级中' : '升级内核' }}
          </button>
        </div>
      </section>

      <div
        v-if="notice"
        class="alert alert-info py-2 text-sm"
      >
        {{ notice }}
      </div>

      <div class="grid gap-3 lg:grid-cols-[minmax(0,1.15fr)_minmax(320px,.85fr)]">
        <section class="base-container p-4">
          <h2 class="mb-4 text-base font-semibold">启动配置</h2>
          <p class="bg-warning/10 text-warning-content mb-4 rounded-box p-3 text-sm leading-6">
            如果配置启用了 TUN，启动内核时会自动请求管理员权限。看到 UAC 提示后允许即可；不需要 TUN 时也可以关闭配置里的 TUN。
          </p>

          <div class="flex flex-col gap-3">
            <label class="form-control">
              <span class="label-text mb-1">内核路径</span>
              <div class="flex gap-2">
                <input
                  v-model="settings.corePath"
                  class="input input-bordered input-sm min-w-0 flex-1"
                  type="text"
                />
                <button
                  class="btn btn-sm"
                  @click="post({ type: 'browseCore' })"
                >
                  选择
                </button>
              </div>
            </label>

            <label class="form-control">
              <span class="label-text mb-1">配置文件</span>
              <div class="flex gap-2">
                <input
                  v-model="settings.configPath"
                  class="input input-bordered input-sm min-w-0 flex-1"
                  type="text"
                />
                <button
                  class="btn btn-sm"
                  @click="post({ type: 'browseConfig' })"
                >
                  选择
                </button>
              </div>
            </label>

            <label class="form-control">
              <span class="label-text mb-1">API 地址</span>
              <div class="flex gap-2">
                <input
                  v-model="settings.apiUrl"
                  class="input input-bordered input-sm min-w-0 flex-1"
                  type="text"
                />
                <button
                  class="btn btn-sm"
                  @click="post({ ...collect(), type: 'reload' })"
                >
                  刷新 UI
                </button>
              </div>
            </label>

            <label class="form-control">
              <span class="label-text mb-1">Secret</span>
              <div class="flex gap-2">
                <input
                  v-model="settings.secret"
                  class="input input-bordered input-sm min-w-0 flex-1"
                  type="text"
                />
                <button
                  class="btn btn-primary btn-sm"
                  @click="saveSettings"
                >
                  保存
                </button>
              </div>
            </label>

            <div class="mt-2 flex flex-col gap-3">
              <label class="flex items-center justify-between gap-3">
                <span class="text-sm">启动软件时自动启动内核</span>
                <input
                  v-model="settings.startCoreOnLaunch"
                  class="toggle toggle-sm"
                  type="checkbox"
                  @change="saveSettings"
                />
              </label>
              <label class="flex items-center justify-between gap-3">
                <span class="text-sm">关闭窗口时最小化到托盘</span>
                <input
                  v-model="settings.minimizeToTray"
                  class="toggle toggle-sm"
                  type="checkbox"
                  @change="saveSettings"
                />
              </label>
              <label class="flex items-center justify-between gap-3">
                <span class="text-sm">开机自启</span>
                <input
                  v-model="settings.autostart"
                  class="toggle toggle-sm"
                  type="checkbox"
                  @change="saveSettings"
                />
              </label>
            </div>
          </div>
        </section>

        <section class="base-container flex min-h-[360px] flex-col p-4">
          <h2 class="mb-4 text-base font-semibold">内核日志</h2>
          <pre class="bg-base-300/60 text-base-content/80 min-h-0 flex-1 overflow-auto rounded-box p-3 text-xs leading-5 whitespace-pre-wrap">{{ runtime.logText || '暂无日志' }}</pre>
        </section>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from 'vue'

type CoreState = {
  isRunning?: boolean
  processId?: number | null
  corePath?: string
  configPath?: string
  apiUrl?: string
  secret?: string
  startCoreOnLaunch?: boolean
  minimizeToTray?: boolean
  autostart?: boolean
  isCoreUpgrading?: boolean
  logText?: string
}

type WebViewWindow = Window & {
  chrome?: {
    webview?: {
      postMessage: (message: unknown) => void
    }
  }
  __mihomoControlSetState?: (state: CoreState) => void
  __mihomoControlNotice?: (message: string) => void
}

const runtime = reactive({
  isRunning: false,
  processId: null as number | null,
  isCoreUpgrading: false,
  logText: '',
})

const settings = reactive({
  corePath: '',
  configPath: '',
  apiUrl: '',
  secret: '',
  startCoreOnLaunch: false,
  minimizeToTray: true,
  autostart: false,
})

const notice = ref('')
let noticeTimer: number | undefined

const webviewWindow = window as WebViewWindow
const post = (message: unknown) => webviewWindow.chrome?.webview?.postMessage(message)

const collect = () => ({
  type: 'save',
  corePath: settings.corePath,
  configPath: settings.configPath,
  apiUrl: settings.apiUrl,
  secret: settings.secret,
  startCoreOnLaunch: settings.startCoreOnLaunch,
  minimizeToTray: settings.minimizeToTray,
  autostart: settings.autostart,
})

const startCore = () => {
  post({ ...collect(), type: 'start' })
}

const saveSettings = () => {
  post(collect())
}

const setState = (state: CoreState) => {
  runtime.isRunning = !!state.isRunning
  runtime.processId = state.processId ?? null
  runtime.isCoreUpgrading = !!state.isCoreUpgrading
  runtime.logText = state.logText ?? ''
  settings.corePath = state.corePath ?? ''
  settings.configPath = state.configPath ?? ''
  settings.apiUrl = state.apiUrl ?? ''
  settings.secret = state.secret ?? ''
  settings.startCoreOnLaunch = !!state.startCoreOnLaunch
  settings.minimizeToTray = !!state.minimizeToTray
  settings.autostart = !!state.autostart
}

const showNotice = (message: string) => {
  notice.value = message
  window.clearTimeout(noticeTimer)
  noticeTimer = window.setTimeout(() => {
    notice.value = ''
  }, 2400)
}

onMounted(() => {
  webviewWindow.__mihomoControlSetState = setState
  webviewWindow.__mihomoControlNotice = showNotice
  post({ type: 'requestState' })
})

onUnmounted(() => {
  if (webviewWindow.__mihomoControlSetState === setState) {
    delete webviewWindow.__mihomoControlSetState
  }
  if (webviewWindow.__mihomoControlNotice === showNotice) {
    delete webviewWindow.__mihomoControlNotice
  }
  window.clearTimeout(noticeTimer)
})
</script>
