<template>
  <SettingItem>
    <div class="setting-item-label">
      {{ $t('customBackgroundURL') }}
    </div>
    <div class="join">
      <TextInput
        class="join-item w-38"
        v-model="customBackgroundURL"
        :clearable="true"
        @update:modelValue="handlerBackgroundURLChange"
      />
      <button
        class="btn join-item btn-sm"
        @click="handlerClickUpload"
      >
        <ArrowUpTrayIcon class="h-4 w-4" />
      </button>
    </div>
    <button
      class="btn btn-circle btn-sm"
      v-if="customBackgroundURL"
      @click="displayBgProperty = !displayBgProperty"
    >
      <AdjustmentsHorizontalIcon class="h-4 w-4" />
    </button>
    <input
      ref="inputFileRef"
      type="file"
      accept="image/*"
      class="hidden"
      @change="handlerFileChange"
    />
  </SettingItem>
  <SettingItem :when="!!customBackgroundURL && displayBgProperty">
    <div class="setting-item-label">
      {{ $t('transparent') }}
    </div>
    <input
      type="range"
      min="0"
      max="100"
      v-model="dashboardTransparent"
      class="range max-w-64"
      @touchstart.passive.stop
      @touchmove.passive.stop
      @touchend.passive.stop
    />
  </SettingItem>
  <SettingItem :when="!!customBackgroundURL && displayBgProperty">
    <div class="setting-item-label">
      {{ $t('blurIntensity') }}
    </div>
    <input
      type="range"
      min="0"
      max="40"
      v-model="blurIntensity"
      class="range max-w-64"
      @touchstart.stop
      @touchmove.stop
      @touchend.stop
    />
  </SettingItem>
</template>

<script setup lang="ts">
import SettingItem from '@/components/settings/SettingItem.vue'
import { replaceBackgroundImage, replaceBackgroundUrl } from '@/helper/backgroundUpdates'
import {
  autoTheme,
  blurIntensity,
  customBackgroundURL,
  dashboardTransparent,
  defaultTheme,
  theme,
} from '@/store/settings'
import { AdjustmentsHorizontalIcon, ArrowUpTrayIcon } from '@heroicons/vue/24/outline'
import { onUnmounted, ref, watch } from 'vue'
import { notifyRequestError } from '@/helper/requestError'
import { useI18n } from 'vue-i18n'
import TextInput from '../../common/TextInput.vue'

type BackgroundToneTheme = 'light' | 'dark'

const { t } = useI18n()

const displayBgProperty = ref(false)
const inputFileRef = ref<HTMLInputElement>()
let mounted = true
onUnmounted(() => {
  mounted = false
})
const reportBackgroundError = (error: unknown) => {
  if (!(error instanceof DOMException && error.name === 'AbortError')) notifyRequestError(error)
}

watch(customBackgroundURL, (value) => {
  if (value) {
    displayBgProperty.value = true
  }
})

const handlerClickUpload = () => {
  inputFileRef.value?.click()
}

const applyThemeByBackgroundTone = (themeName: BackgroundToneTheme) => {
  autoTheme.value = false
  defaultTheme.value = themeName
}

const detectCurrentThemeTone = (): BackgroundToneTheme | null => {
  const themeElement = document.createElement('div')
  themeElement.dataset.theme = theme.value
  themeElement.style.display = 'none'
  document.body.appendChild(themeElement)

  const styles = getComputedStyle(themeElement)
  const colorScheme = styles.getPropertyValue('color-scheme').trim() || styles.colorScheme.trim()

  themeElement.remove()

  if (colorScheme === 'dark') {
    return 'dark'
  }

  return 'light'
}

const confirmApplyThemeByBackgroundTone = (themeName: BackgroundToneTheme) => {
  if (detectCurrentThemeTone() === themeName) {
    return
  }

  const toneLabel =
    themeName === 'dark' ? t('backgroundToneDarkThemeLabel') : t('backgroundToneLightThemeLabel')

  if (!window.confirm(t('backgroundToneSwitchConfirm', { theme: toneLabel }))) {
    return
  }

  applyThemeByBackgroundTone(themeName)
}

const detectBackgroundTone = (imageURL: string, signal: AbortSignal) => {
  return new Promise<BackgroundToneTheme | null>((resolve, reject) => {
    const image = new Image()
    const abort = () => {
      cleanup()
      image.src = ''
      reject(new DOMException('Image update cancelled', 'AbortError'))
    }
    const cleanup = () => {
      signal.removeEventListener('abort', abort)
      image.onload = null
      image.onerror = null
    }
    if (signal.aborted) {
      abort()
      return
    }
    signal.addEventListener('abort', abort, { once: true })

    image.onload = () => {
      cleanup()
      try {
        const canvas = document.createElement('canvas')
        const ctx = canvas.getContext('2d', { willReadFrequently: true })

        if (!ctx) {
          resolve(null)
          return
        }

        const sampleSize = 48
        canvas.width = sampleSize
        canvas.height = sampleSize
        ctx.drawImage(image, 0, 0, sampleSize, sampleSize)

        const { data } = ctx.getImageData(0, 0, sampleSize, sampleSize)
        let weightedBrightness = 0
        let visiblePixels = 0

        for (let i = 0; i < data.length; i += 4) {
          const alpha = data[i + 3] / 255
          if (alpha === 0) continue

          const r = data[i]
          const g = data[i + 1]
          const b = data[i + 2]

          weightedBrightness += (0.299 * r + 0.587 * g + 0.114 * b) * alpha
          visiblePixels += alpha
        }

        if (visiblePixels === 0) {
          resolve('light')
          return
        }

        const averageBrightness = weightedBrightness / visiblePixels
        resolve(averageBrightness < 140 ? 'dark' : 'light')
      } catch {
        // The image decoded, but tone sampling (for example SVG/CORS) is unavailable.
        resolve(null)
      }
    }

    image.onerror = () => {
      cleanup()
      reject(new Error('Failed to load background image'))
    }
    image.src = imageURL
  })
}

const handlerBackgroundURLChange = () => {
  void replaceBackgroundUrl(customBackgroundURL.value).catch(reportBackgroundError)
}

const readImage = (file: File, signal: AbortSignal) =>
  new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    const abort = () => {
      reader.abort()
      cleanup()
      reject(new DOMException('Image update cancelled', 'AbortError'))
    }
    const cleanup = () => {
      signal.removeEventListener('abort', abort)
      reader.onload = null
      reader.onerror = null
    }
    if (signal.aborted) {
      abort()
      return
    }
    signal.addEventListener('abort', abort, { once: true })
    reader.onload = () => {
      cleanup()
      resolve(reader.result as string)
    }
    reader.onerror = () => {
      cleanup()
      reject(reader.error ?? new Error('Failed to read background image'))
    }
    reader.readAsDataURL(file)
  })

const handlerFileChange = (e: Event) => {
  const target = e.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return
  target.value = ''
  void replaceBackgroundImage(async (signal, current) => {
    const imageURL = await readImage(file, signal)
    const tone = await detectBackgroundTone(imageURL, signal)
    if (tone && current() && mounted) confirmApplyThemeByBackgroundTone(tone)
    return imageURL
  }).catch(reportBackgroundError)
}
</script>
