<template>
  <div class="settings-section-label">
    {{ $t('appearance') }}
  </div>
  <div class="settings-grid">
    <div class="setting-item">
      <div class="setting-item-label">
        {{ $t('autoSwitchTheme') }}
      </div>
      <input
        type="checkbox"
        v-model="autoTheme"
        class="toggle"
      />
    </div>
    <div class="setting-item">
      <div class="setting-item-label">
        {{ $t('defaultTheme') }}
      </div>
      <div class="join">
        <ThemeSelector
          class="w-38!"
          v-model:value="defaultTheme"
        />
        <button
          class="btn btn-sm join-item"
          @click="customThemeModal = !customThemeModal"
        >
          <PlusIcon class="h-4 w-4" />
        </button>
      </div>
      <CustomTheme v-model:value="customThemeModal" />
    </div>
    <div
      v-if="autoTheme"
      class="setting-item"
    >
      <div class="setting-item-label">
        {{ $t('darkTheme') }}
      </div>
      <ThemeSelector v-model:value="darkTheme" />
    </div>
    <BackgroundSettings />
    <div class="setting-item">
      <div class="setting-item-label">
        {{ $t('fonts') }}
      </div>
      <select
        class="select select-sm w-48"
        v-model="font"
      >
        <option
          v-for="opt in fontOptions"
          :key="opt"
          :value="opt"
        >
          {{ opt }}
        </option>
      </select>
    </div>
    <div class="setting-item">
      <div class="setting-item-label">Emoji</div>
      <select
        class="select select-sm w-48"
        v-model="emoji"
      >
        <option
          v-for="opt in Object.values(EMOJIS)"
          :key="opt"
          :value="opt"
        >
          {{ opt }}
        </option>
      </select>
    </div>
  </div>
</template>

<script setup lang="ts">
import { EMOJIS, FONTS } from '@/constant'
import { autoTheme, darkTheme, defaultTheme, emoji, font } from '@/store/settings'
import { PlusIcon } from '@heroicons/vue/24/outline'
import { computed, ref } from 'vue'
import BackgroundSettings from './BackgroundSettings.vue'
import CustomTheme from './CustomTheme.vue'
import ThemeSelector from './ThemeSelector.vue'

const customThemeModal = ref(false)

const fontOptions = computed(() => {
  const mode = import.meta.env.MODE

  if (Object.values(FONTS).includes(mode as FONTS)) {
    return [mode]
  }

  return Object.values(FONTS)
})
</script>
