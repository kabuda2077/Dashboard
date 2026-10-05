<script setup lang="ts">
import { computed, onMounted, onScopeDispose, ref, type Ref, watch } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import { startBackendRuntime } from '@/helper/backendRuntime'
import ConfirmDialogHost from './components/common/ConfirmDialogHost.vue'
import { useAppearanceVars } from './composables/useAppearanceVars'
import { useKeyboard } from './composables/keyboard'
import { importStartupSettings } from './helper/appStartup'
import { EMOJIS, FONTS } from './constant'
import { backgroundImage } from './helper/indexeddb'
import { initNotification } from './helper/notification'
import { isPreferredDark } from './helper/utils'
import { disablePullToRefresh, emoji, font, theme } from './store/settings'

const app = ref<HTMLElement>()
const toast = ref<HTMLElement>()

initNotification(toast as Ref<HTMLElement>)
useAppearanceVars()
useKeyboard()
const runtimeRoute = useRoute()
startBackendRuntime(() => runtimeRoute.name)

let touchX = 0,
  touchY = 0
const previousOverscroll = document.body.style.overscrollBehavior
const touchStart = (event: TouchEvent) => {
  if (!event.touches.length) return
  touchX = event.touches[0].clientX
  touchY = event.touches[0].clientY
}
const touchMove = (event: TouchEvent) => {
  if (event.touches.length !== 1 || !event.cancelable) return
  const dx = event.touches[0].clientX - touchX,
    dy = event.touches[0].clientY - touchY
  if (Math.abs(dx) >= Math.abs(dy)) return
  let element = event.target instanceof HTMLElement ? event.target : null
  if (element?.closest('input,textarea,select,[contenteditable="true"]')) return
  while (element && element !== document.body) {
    const overflow = getComputedStyle(element).overflowY
    if (['auto', 'scroll'].includes(overflow) && element.scrollHeight > element.clientHeight) {
      if (
        (dy > 0 && element.scrollTop > 0) ||
        (dy < 0 && element.scrollTop + element.clientHeight < element.scrollHeight - 1)
      )
        return
      break
    }
    element = element.parentElement
  }
  event.preventDefault()
}
const removeTouchLock = () => {
  document.removeEventListener('touchstart', touchStart)
  document.removeEventListener('touchmove', touchMove)
  document.body.style.overscrollBehavior = previousOverscroll
}
watch(
  disablePullToRefresh,
  (enabled) => {
    removeTouchLock()
    if (enabled) {
      document.body.style.overscrollBehavior = 'none'
      document.addEventListener('touchstart', touchStart, { passive: true })
      document.addEventListener('touchmove', touchMove, { passive: false })
    }
  },
  { immediate: true },
)
onScopeDispose(removeTouchLock)

const FONT_CLASS_MAP = {
  [EMOJIS.TWEMOJI]: {
    [FONTS.MI_SANS]: 'font-MiSans-Twemoji',
    [FONTS.SARASA_UI]: 'font-SarasaUI-Twemoji',
    [FONTS.PING_FANG]: 'font-PingFang-Twemoji',
    [FONTS.FIRA_SANS]: 'font-FiraSans-Twemoji',
    [FONTS.SYSTEM_UI]: 'font-SystemUI-Twemoji',
  },
  [EMOJIS.NOTO_COLOR_EMOJI]: {
    [FONTS.MI_SANS]: 'font-MiSans-NotoEmoji',
    [FONTS.SARASA_UI]: 'font-SarasaUI-NotoEmoji',
    [FONTS.PING_FANG]: 'font-PingFang-NotoEmoji',
    [FONTS.FIRA_SANS]: 'font-FiraSans-NotoEmoji',
    [FONTS.SYSTEM_UI]: 'font-SystemUI-NotoEmoji',
  },
} as const

const fontClassName = computed(
  () =>
    FONT_CLASS_MAP[emoji.value]?.[font.value] || FONT_CLASS_MAP[EMOJIS.TWEMOJI][FONTS.SYSTEM_UI],
)

const setThemeColor = () => {
  if (!app.value) return
  const themeColor = getComputedStyle(app.value).getPropertyValue('background-color').trim()
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', themeColor)
}

watch(isPreferredDark, setThemeColor)
watch(
  theme,
  () => {
    document.body.setAttribute('data-theme', theme.value)
    setThemeColor()
  },
  { immediate: true },
)

onMounted(() => {
  setThemeColor()
  void importStartupSettings()
})
</script>

<template>
  <div
    ref="app"
    id="app-content"
    :class="[
      'bg-base-100 flex w-screen overflow-hidden',
      fontClassName,
      backgroundImage && 'custom-background bg-cover bg-center',
    ]"
    :style="[backgroundImage, { height: 'var(--app-height, 100dvh)' }]"
  >
    <RouterView />
    <ConfirmDialogHost />
    <div
      ref="toast"
      class="app-toast-region"
    />
  </div>
</template>
