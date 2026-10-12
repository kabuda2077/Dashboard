<script setup lang="ts">
import { computed, onMounted, ref, type Ref, watch } from 'vue'
import { RouterView, useRoute } from 'vue-router'
import { startBackendRuntime } from '@/helper/backendRuntime'
import ConfirmDialogHost from './components/common/ConfirmDialogHost.vue'
import { useAppearanceVars } from './composables/use-appearance-vars'
import { useKeyboard } from './composables/use-keyboard'
import { importStartupSettings } from './helper/appStartup'
import { EMOJIS, FONTS } from './constant'
import { backgroundImage } from './helper/indexeddb'
import { initNotification } from './helper/notification'
import { emoji, font, theme } from './store/settings'
const toast = ref<HTMLElement>()

initNotification(toast as Ref<HTMLElement>)
useAppearanceVars()
useKeyboard()
const runtimeRoute = useRoute()
startBackendRuntime(() => runtimeRoute.name)

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

watch(
  theme,
  () => document.documentElement.setAttribute('data-theme', theme.value),
  { immediate: true },
)

onMounted(() => {
  void importStartupSettings()
})
</script>

<template>
  <div
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
