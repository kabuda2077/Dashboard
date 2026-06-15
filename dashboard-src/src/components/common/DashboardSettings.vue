<template>
  <div class="settings-section-label flex items-center justify-between gap-3 !pt-0">
    <span>{{ $t('dashboardSettingsJsonFile') }}</span>
    <button
      class="btn btn-xs"
      @click="handlerClickResetSettings"
    >
      {{ $t('resetSettings') }}
    </button>
  </div>
  <div class="settings-grid">
    <div class="setting-item">
      <div class="setting-item-label">
        {{ $t('exportSettings') }}
      </div>
      <button
        class="btn btn-sm"
        @click="exportSettings"
      >
        {{ $t('exportSettings') }}
        <ArrowDownCircleIcon class="h-4 w-4" />
      </button>
    </div>
    <div class="setting-item">
      <div class="setting-item-label">
        {{ $t('importFromFile') }}
      </div>
      <button
        class="btn btn-sm"
        @click="importSettingsFromFile"
      >
        {{ $t('importFromFile') }}
        <ArrowUpCircleIcon class="h-4 w-4" />
      </button>
    </div>
  </div>

  <div class="settings-section-label">
    {{ $t('dashboardSettingsUrl') }}
  </div>
  <div class="settings-grid">
    <div class="setting-item max-sm:flex-col max-sm:items-start! max-sm:py-3">
      <div class="setting-item-label shrink-0!">
        {{ $t('importFromUrl') }}
      </div>
      <div class="flex items-center gap-2 max-sm:flex-wrap">
        <div class="join flex-1">
          <TextInput
            v-model="importSettingsUrl"
            class="max-w-none flex-1"
          />
          <button
            class="btn btn-sm join-item"
            @click="importSettingsFromUrlHandler()"
          >
            <ArrowDownTrayIcon class="h-4 w-4" />
          </button>
        </div>
        <QuestionMarkCircleIcon
          v-if="importSettingsUrl === DEFAULT_SETTINGS_URL"
          class="h-4 w-4 shrink-0"
          @mouseenter="
            showTip($event, $t('importFromBackendTip'), {
              appendTo: 'parent',
            })
          "
        />
        <button
          v-else
          class="btn btn-sm"
          @click="importSettingsUrl = DEFAULT_SETTINGS_URL"
        >
          {{ $t('reset') }}
        </button>
      </div>
    </div>
    <div class="setting-item">
      <div class="setting-item-label flex items-center gap-2">
        {{ $t('autoImportFromUrl') }}
        <QuestionMarkCircleIcon
          class="h-4 w-4 cursor-pointer"
          @mouseenter="
            showTip($event, $t('autoImportFromUrlTip'), {
              appendTo: 'parent',
            })
          "
        />
      </div>
      <input
        v-model="autoImportSettings"
        type="checkbox"
        class="toggle"
      />
    </div>
  </div>
  <input
    ref="inputRef"
    type="file"
    accept=".json"
    class="hidden"
    @change="handlerJsonUpload"
  />
</template>

<script setup lang="ts">
import {
  autoImportSettings,
  DEFAULT_SETTINGS_URL,
  importSettingsFromUrl,
  importSettingsUrl,
} from '@/helper/autoImportSettings'
import { showNotification } from '@/helper/notification'
import { useTooltip } from '@/helper/tooltip'
import { applyDashboardSettingsToStorage, exportSettings, resetSettings } from '@/helper/utils'
import {
  ArrowDownCircleIcon,
  ArrowDownTrayIcon,
  ArrowUpCircleIcon,
  QuestionMarkCircleIcon,
} from '@heroicons/vue/24/outline'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import TextInput from './TextInput.vue'

const inputRef = ref<HTMLInputElement>()

const { showTip } = useTooltip()
const { t } = useI18n()

const handlerClickResetSettings = () => {
  if (!window.confirm(t('resetSettingsConfirm'))) return
  resetSettings()
}

const handlerJsonUpload = () => {
  showNotification({
    content: 'importing',
  })
  const file = inputRef.value?.files?.[0]
  if (!file) return
  const reader = new FileReader()
  reader.onload = async () => {
    const settings = JSON.parse(reader.result as string)
    applyDashboardSettingsToStorage(settings)
    location.reload()
  }
  reader.readAsText(file)
}

const importSettingsFromFile = () => {
  inputRef.value?.click()
}
const importSettingsFromUrlHandler = async () => {
  await importSettingsFromUrl(true)
}
</script>
