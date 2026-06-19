<template>
  <div class="h-full overflow-x-hidden overflow-y-auto">
    <CtrlsBar>
      <div
        class="pointer-events-auto fixed top-3 flex min-h-9 max-w-full min-w-0 items-center gap-2 text-sm"
        :style="{
          left: topControlsLeft === null ? undefined : `${topControlsLeft}px`,
          width: topControlsWidth === null ? undefined : `${topControlsWidth}px`,
          maxWidth: '100%',
        }"
      >
        <div class="relative flex h-9 min-w-0 flex-1 items-center">
          <span
            class="absolute top-1/2 -left-7 h-3 w-3 -translate-y-1/2 rounded-full"
            :class="
              runtime.isRunning
                ? 'bg-success shadow-success/30 shadow-[0_0_0_4px]'
                : 'bg-warning shadow-warning/30 shadow-[0_0_0_4px]'
            "
          />
          <div
            class="border-base-content/20 bg-base-100 flex h-9 w-full min-w-0 items-center gap-3 rounded-lg border px-3 pr-4 shadow-none"
          >
            <span class="font-semibold whitespace-nowrap">{{ coreTitle }}</span>
            <span class="text-base-content/60 text-xs whitespace-nowrap">
              {{ runtimeStatusText }}
            </span>
          </div>
        </div>

        <div class="flex h-[34px] shrink-0 items-center gap-2">
          <button
            class="btn btn-primary btn-sm !h-[34px] !min-h-[34px] w-[72px] !gap-1 rounded-lg !px-2 text-sm leading-none whitespace-nowrap"
            :disabled="runtime.isCoreUpgrading || runtime.isCoreSwitching"
            @click="showSwitchConfirm = true"
          >
            <ArrowsRightLeftIcon class="h-3.5 w-3.5" />
            切换
          </button>
          <button
            class="btn btn-primary btn-sm !h-[34px] !min-h-[34px] w-[72px] !gap-1 rounded-lg !px-2 text-sm leading-none whitespace-nowrap"
            :disabled="runtime.isRunning || runtime.isCoreUpgrading || runtime.isCoreSwitching"
            @click="startCore"
          >
            <PlayIcon class="h-3.5 w-3.5" />
            启动
          </button>
          <button
            class="btn btn-warning btn-sm !h-[34px] !min-h-[34px] w-[72px] !gap-1 rounded-lg !px-2 text-sm leading-none whitespace-nowrap"
            :disabled="!runtime.isRunning || runtime.isCoreUpgrading || runtime.isCoreSwitching"
            @click="post({ type: 'stop' })"
          >
            <StopIcon class="h-3.5 w-3.5" />
            停止
          </button>
        </div>
      </div>
    </CtrlsBar>
    <div
      class="mx-auto flex w-full max-w-7xl flex-col gap-3 p-3"
      :style="padding"
    >
      <div class="grid items-start gap-3 lg:grid-cols-2 lg:gap-12">
        <section class="rounded-lg p-2">
          <h2 class="mt-1 mb-3 px-1 text-lg font-semibold">启动配置</h2>
          <div
            ref="configPanelRef"
            class="settings-grid"
          >
            <div class="setting-item !gap-0">
              <div class="setting-item-label w-[4.5rem] !flex-none shrink-0">内核路径</div>
              <div class="flex min-w-0 flex-1 items-center gap-2">
                <input
                  v-model="activeCorePath"
                  class="input input-sm bg-base-200/70 text-base-content/60 min-w-0 flex-1 border-transparent shadow-none focus:border-transparent"
                  type="text"
                />
                <button
                  class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
                  @click="post({ ...collect(), type: 'browseCore' })"
                >
                  选择
                </button>
                <button
                  class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
                  @click="post({ ...collect(), type: 'openCoreLocation' })"
                >
                  位置
                </button>
              </div>
            </div>

            <div class="setting-item !gap-0">
              <div class="setting-item-label w-[4.5rem] !flex-none shrink-0">配置文件</div>
              <div class="flex min-w-0 flex-1 items-center gap-2">
                <input
                  v-model="activeConfigPath"
                  class="input input-sm bg-base-200/70 text-base-content/60 min-w-0 flex-1 border-transparent shadow-none focus:border-transparent"
                  type="text"
                />
                <button
                  class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
                  @click="post({ ...collect(), type: 'browseConfig' })"
                >
                  选择
                </button>
                <button
                  class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
                  @click="post({ ...collect(), type: 'openConfigLocation' })"
                >
                  位置
                </button>
              </div>
            </div>

            <div class="setting-item !gap-0">
              <div class="setting-item-label w-[4.5rem] !flex-none shrink-0">API 地址</div>
              <div class="flex min-w-0 flex-1 items-center gap-2">
                <input
                  v-model="activeApiUrl"
                  class="input input-sm bg-base-200/70 text-base-content/60 min-w-0 flex-1 border-transparent shadow-none focus:border-transparent"
                  type="text"
                />
              </div>
            </div>

            <div class="setting-item !gap-0">
              <div class="setting-item-label w-[4.5rem] !flex-none shrink-0">Secret</div>
              <div class="flex min-w-0 flex-1 items-center gap-2">
                <input
                  v-model="activeSecret"
                  class="input input-sm bg-base-200/70 text-base-content/60 min-w-0 flex-1 border-transparent shadow-none focus:border-transparent"
                  type="text"
                />
                <button
                  class="btn btn-primary btn-sm"
                  @click="saveSettings"
                >
                  保存
                </button>
              </div>
            </div>

            <div class="setting-item">
              <div class="setting-item-label">启动软件时自动启动内核</div>
              <input
                v-model="settings.startCoreOnLaunch"
                class="toggle"
                type="checkbox"
                @change="saveSettings"
              />
            </div>
            <div class="setting-item">
              <div class="setting-item-label">关闭窗口时隐藏到托盘</div>
              <input
                v-model="settings.minimizeToTray"
                class="toggle"
                type="checkbox"
                @change="saveSettings"
              />
            </div>
            <div class="setting-item">
              <div class="setting-item-label">轻量模式</div>
              <input
                v-model="settings.lightweightMode"
                class="toggle"
                type="checkbox"
                @change="saveSettings"
              />
            </div>
            <div class="setting-item">
              <div class="setting-item-label">开机自启</div>
              <input
                v-model="settings.autostart"
                class="toggle"
                type="checkbox"
                @change="saveSettings"
              />
            </div>
          </div>
        </section>

        <section class="rounded-lg p-2">
          <h2 class="mt-1 mb-3 px-1 text-lg font-semibold">内核日志</h2>
          <div
            ref="logPanelRef"
            class="settings-grid p-4"
            :style="logPanelHeight ? { height: `${logPanelHeight}px` } : undefined"
          >
            <pre
              class="bg-base-200/70 text-base-content/60 rounded-box h-full min-h-0 overflow-auto p-3 text-xs leading-5 whitespace-pre-wrap"
              >{{ runtime.logText || '暂无日志' }}</pre
            >
          </div>
        </section>
      </div>

      <SettingsContent
        embedded
        id-prefix="core-settings"
        :scroll-to="settingsScrollTo"
      />
    </div>

    <div
      v-if="showSwitchConfirm"
      class="modal modal-open"
    >
      <div class="modal-box max-w-sm rounded-lg">
        <h3 class="text-lg font-semibold">切换内核</h3>
        <p class="text-base-content/60 mt-2 text-sm leading-6">
          将停止当前内核，等待进程完全退出后启动 {{ nextCoreTitle }}。
        </p>
        <div class="modal-action">
          <button
            class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
            :disabled="switchPending"
            @click="showSwitchConfirm = false"
          >
            取消
          </button>
          <button
            class="btn btn-primary btn-sm"
            :disabled="switchPending"
            @click="confirmSwitchCore"
          >
            确定
          </button>
        </div>
      </div>
      <form
        method="dialog"
        class="modal-backdrop"
      >
        <button
          aria-label="关闭"
          @click.prevent="showSwitchConfirm = false"
        />
      </form>
    </div>
  </div>
</template>

<script setup lang="ts">
import CtrlsBar from '@/components/common/CtrlsBar.vue'
import SettingsContent from '@/components/settings/SettingsContent.vue'
import { coreHostActionsKey } from '@/composables/coreHostActions'
import { usePaddingForViews } from '@/composables/paddingViews'
import { showNotification } from '@/helper/notification'
import { isSidebarCollapsed } from '@/store/settings'
import { ArrowsRightLeftIcon, PlayIcon, StopIcon } from '@heroicons/vue/24/outline'
import { computed, nextTick, onMounted, onUnmounted, provide, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

type CoreState = {
  isRunning?: boolean
  processId?: number | null
  coreType?: string
  coreTitle?: string
  corePath?: string
  configPath?: string
  apiUrl?: string
  secret?: string
  mihomoCorePath?: string
  mihomoConfigPath?: string
  mihomoApiUrl?: string
  mihomoSecret?: string
  singBoxCorePath?: string
  singBoxConfigPath?: string
  singBoxApiUrl?: string
  singBoxSecret?: string
  startCoreOnLaunch?: boolean
  minimizeToTray?: boolean
  lightweightMode?: boolean
  autostart?: boolean
  canUpgradeCore?: boolean
  isCoreUpgrading?: boolean
  isCoreSwitching?: boolean
  logText?: string
  iconCacheMap?: Record<string, string>
}

type HostMessage = {
  type?: string
  state?: CoreState
  message?: string
}

type WebViewWindow = Window & {
  chrome?: {
    webview?: {
      postMessage: (message: unknown) => void
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
  __mihomoControlSetState?: (state: CoreState) => void
  __mihomoControlNotice?: (message: string) => void
}

const { padding } = usePaddingForViews({
  offsetTop: 12,
  offsetBottom: 0,
})

const runtime = reactive({
  isRunning: false,
  processId: null as number | null,
  coreTitle: 'Mihomo Core',
  canUpgradeCore: true,
  isCoreUpgrading: false,
  isCoreSwitching: false,
  logText: '',
})

const settings = reactive({
  coreType: 'mihomo',
  mihomoCorePath: '',
  mihomoConfigPath: '',
  mihomoApiUrl: '',
  mihomoSecret: '',
  singBoxCorePath: '',
  singBoxConfigPath: '',
  singBoxApiUrl: '',
  singBoxSecret: '',
  startCoreOnLaunch: false,
  minimizeToTray: true,
  lightweightMode: true,
  autostart: false,
})

const configPanelRef = ref<HTMLElement>()
const logPanelRef = ref<HTMLElement>()
const logPanelHeight = ref<number | null>(null)
const topControlsWidth = ref<number | null>(null)
const topControlsLeft = ref<number | null>(null)
const statusDotInset = 28
const statusDotOpticalOffset = 2
const windowControlsReserve = 176
const chromeRightPadding = 12
const showSwitchConfirm = ref(false)
const switchPending = ref(false)
let resizeObserver: ResizeObserver | undefined
let syncFrame = 0
let sidebarSyncRaf = 0
let stopSidebarWatch: (() => void) | undefined

const webviewWindow = window as WebViewWindow
const post = (message: unknown) => webviewWindow.chrome?.webview?.postMessage(message)
const route = useRoute()
const settingsScrollTo = computed(() =>
  typeof route.query.scrollTo === 'string' ? route.query.scrollTo : null,
)

const runtimeStatusText = computed(() => {
  if (!runtime.isRunning) {
    return '未运行'
  }

  return runtime.processId ? `运行中 / PID ${runtime.processId}` : '运行中'
})

const normalizeCoreType = (coreType: string | undefined) =>
  coreType === 'sing-box' ? 'sing-box' : 'mihomo'

const coreTitle = computed(() => runtime.coreTitle || (settings.coreType === 'sing-box' ? 'sing-box' : 'Mihomo Core'))
const nextCoreType = computed(() => (settings.coreType === 'sing-box' ? 'mihomo' : 'sing-box'))
const nextCoreTitle = computed(() => (nextCoreType.value === 'sing-box' ? 'sing-box' : 'Mihomo Core'))

const activeCorePath = computed({
  get: () => (settings.coreType === 'sing-box' ? settings.singBoxCorePath : settings.mihomoCorePath),
  set: (value: string) => {
    if (settings.coreType === 'sing-box') {
      settings.singBoxCorePath = value
    } else {
      settings.mihomoCorePath = value
    }
  },
})

const activeConfigPath = computed({
  get: () =>
    settings.coreType === 'sing-box' ? settings.singBoxConfigPath : settings.mihomoConfigPath,
  set: (value: string) => {
    if (settings.coreType === 'sing-box') {
      settings.singBoxConfigPath = value
    } else {
      settings.mihomoConfigPath = value
    }
  },
})

const activeApiUrl = computed({
  get: () => (settings.coreType === 'sing-box' ? settings.singBoxApiUrl : settings.mihomoApiUrl),
  set: (value: string) => {
    if (settings.coreType === 'sing-box') {
      settings.singBoxApiUrl = value
    } else {
      settings.mihomoApiUrl = value
    }
  },
})

const activeSecret = computed({
  get: () => (settings.coreType === 'sing-box' ? settings.singBoxSecret : settings.mihomoSecret),
  set: (value: string) => {
    if (settings.coreType === 'sing-box') {
      settings.singBoxSecret = value
    } else {
      settings.mihomoSecret = value
    }
  },
})

const collect = () => ({
  type: 'save',
  coreType: settings.coreType,
  corePath: activeCorePath.value,
  configPath: activeConfigPath.value,
  apiUrl: activeApiUrl.value,
  secret: activeSecret.value,
  mihomoCorePath: settings.mihomoCorePath,
  mihomoConfigPath: settings.mihomoConfigPath,
  mihomoApiUrl: settings.mihomoApiUrl,
  mihomoSecret: settings.mihomoSecret,
  singBoxCorePath: settings.singBoxCorePath,
  singBoxConfigPath: settings.singBoxConfigPath,
  singBoxApiUrl: settings.singBoxApiUrl,
  singBoxSecret: settings.singBoxSecret,
  startCoreOnLaunch: settings.startCoreOnLaunch,
  minimizeToTray: settings.minimizeToTray,
  lightweightMode: settings.lightweightMode,
  autostart: settings.autostart,
})

const startCore = () => {
  post({ ...collect(), type: 'start' })
}

const confirmSwitchCore = () => {
  switchPending.value = true
  post({ ...collect(), type: 'switchCore', targetCoreType: nextCoreType.value })
}

const restartCore = () => {
  post({ ...collect(), type: 'restart' })
}

const upgradeCore = () => {
  if (!runtime.canUpgradeCore) {
    return
  }

  post({ ...collect(), type: 'upgradeCore' })
}

const saveSettings = () => {
  post(collect())
}

provide(coreHostActionsKey, {
  isRunning: computed(() => runtime.isRunning),
  isCoreUpgrading: computed(() => runtime.isCoreUpgrading),
  canUpgradeCore: computed(() => runtime.canUpgradeCore),
  restartCore,
  upgradeCore,
})

const setState = (state: CoreState) => {
  runtime.isRunning = !!state.isRunning
  runtime.processId = state.processId ?? null
  settings.coreType = normalizeCoreType(state.coreType)
  runtime.coreTitle = state.coreTitle ?? (settings.coreType === 'sing-box' ? 'sing-box' : 'Mihomo Core')
  runtime.canUpgradeCore = state.canUpgradeCore ?? settings.coreType !== 'sing-box'
  runtime.isCoreUpgrading = !!state.isCoreUpgrading
  runtime.isCoreSwitching = !!state.isCoreSwitching
  runtime.logText = state.logText ?? ''
  settings.mihomoCorePath = state.mihomoCorePath ?? (settings.coreType === 'mihomo' ? state.corePath : '') ?? ''
  settings.mihomoConfigPath =
    state.mihomoConfigPath ?? (settings.coreType === 'mihomo' ? state.configPath : '') ?? ''
  settings.mihomoApiUrl = state.mihomoApiUrl ?? (settings.coreType === 'mihomo' ? state.apiUrl : '') ?? ''
  settings.mihomoSecret =
    state.mihomoSecret ?? (settings.coreType === 'mihomo' ? state.secret : '') ?? ''
  settings.singBoxCorePath =
    state.singBoxCorePath ?? (settings.coreType === 'sing-box' ? state.corePath : '') ?? ''
  settings.singBoxConfigPath =
    state.singBoxConfigPath ?? (settings.coreType === 'sing-box' ? state.configPath : '') ?? ''
  settings.singBoxApiUrl =
    state.singBoxApiUrl ?? (settings.coreType === 'sing-box' ? state.apiUrl : '') ?? ''
  settings.singBoxSecret =
    state.singBoxSecret ?? (settings.coreType === 'sing-box' ? state.secret : '') ?? ''
  settings.startCoreOnLaunch = !!state.startCoreOnLaunch
  settings.minimizeToTray = !!state.minimizeToTray
  settings.lightweightMode = state.lightweightMode ?? true
  settings.autostart = !!state.autostart
  if (!runtime.isCoreSwitching) {
    switchPending.value = false
    showSwitchConfirm.value = false
  }
}

const getNoticeType = (message: string) => {
  if (message.startsWith('操作失败') || message.includes('失败')) {
    return 'alert-error'
  }

  if (message.startsWith('正在')) {
    return 'alert-info'
  }

  if (message.includes('管理员权限') || message.includes('UAC')) {
    return 'alert-warning'
  }

  return 'alert-success'
}

const showNotice = (message: string) => {
  if (!message) {
    return
  }

  showNotification({
    content: message,
    key: `core-host-${message}`,
    type: getNoticeType(message),
  })
}

const syncLogHeight = () => {
  window.cancelAnimationFrame(syncFrame)
  syncFrame = window.requestAnimationFrame(() => {
    const configPanel = configPanelRef.value
    const logPanel = logPanelRef.value
    if (!configPanel) {
      return
    }

    const panelRect = configPanel.getBoundingClientRect()
    const rowLeft = Math.round(panelRect.left + statusDotInset + statusDotOpticalOffset)
    const panelRight = Math.round(panelRect.right)
    topControlsLeft.value = rowLeft

    const desiredWidth = Math.round(panelRight - rowLeft)
    const availableRight = window.innerWidth - windowControlsReserve - chromeRightPadding
    const availableWidth = Math.floor(availableRight - rowLeft)
    topControlsWidth.value = Math.max(280, Math.min(desiredWidth, availableWidth))

    if (logPanel) {
      logPanelHeight.value = Math.round(configPanel.offsetHeight)
    }
  })
}

const syncAroundSidebarTransition = () => {
  window.cancelAnimationFrame(sidebarSyncRaf)
  const startedAt = performance.now()
  const tick = () => {
    syncLogHeight()
    if (performance.now() - startedAt < 520) {
      sidebarSyncRaf = window.requestAnimationFrame(tick)
    }
  }
  tick()
}

const handleHostMessage = (event: MessageEvent<HostMessage>) => {
  if (event.data?.type === 'state') {
    setState(event.data.state ?? {})
  } else if (event.data?.type === 'notice') {
    showNotice(event.data.message ?? '')
  }
}

onMounted(async () => {
  webviewWindow.chrome?.webview?.addEventListener?.('message', handleHostMessage)
  webviewWindow.__mihomoControlSetState = setState
  webviewWindow.__mihomoControlNotice = showNotice
  await nextTick()
  syncLogHeight()
  window.addEventListener('resize', syncLogHeight)
  stopSidebarWatch = watch(isSidebarCollapsed, syncAroundSidebarTransition)
  if (configPanelRef.value) {
    resizeObserver = new ResizeObserver(syncLogHeight)
    resizeObserver.observe(configPanelRef.value)
  }
  post({ type: 'requestState' })
})

onUnmounted(() => {
  webviewWindow.chrome?.webview?.removeEventListener?.('message', handleHostMessage)
  if (webviewWindow.__mihomoControlSetState === setState) {
    delete webviewWindow.__mihomoControlSetState
  }
  if (webviewWindow.__mihomoControlNotice === showNotice) {
    delete webviewWindow.__mihomoControlNotice
  }
  window.removeEventListener('resize', syncLogHeight)
  stopSidebarWatch?.()
  stopSidebarWatch = undefined
  window.cancelAnimationFrame(sidebarSyncRaf)
  resizeObserver?.disconnect()
  window.cancelAnimationFrame(syncFrame)
})
</script>
