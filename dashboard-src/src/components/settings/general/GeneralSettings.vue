<template>
  <div class="settings-section-label">
    {{ $t('general') }}
  </div>
  <div class="settings-grid">
    <LanguageSelect />
    <div class="setting-item">
      <div class="setting-item-label">
        {{ $t('autoDisconnectIdleUDP') }}
        <QuestionMarkCircleIcon
          class="h-4 w-4 cursor-pointer"
          @mouseenter="showTip($event, $t('autoDisconnectIdleUDPTip'))"
        />
      </div>
      <input
        type="checkbox"
        v-model="autoDisconnectIdleUDP"
        class="toggle"
      />
    </div>
    <div
      v-if="autoDisconnectIdleUDP"
      class="setting-item"
    >
      <div class="setting-item-label">
        {{ $t('autoDisconnectIdleUDPTime') }}
      </div>
      <input
        type="number"
        class="input input-sm w-20"
        v-model="autoDisconnectIdleUDPTime"
      />
      mins
    </div>
    <div class="setting-item">
      <div class="setting-item-label">
        {{ $t('IPInfoAPI') }}
        <QuestionMarkCircleIcon
          class="h-4 w-4 cursor-pointer"
          @mouseenter="showTip($event, $t('IPInfoAPITip'))"
        />
      </div>
      <select
        class="select select-sm min-w-24"
        v-model="IPInfoAPI"
      >
        <option
          v-for="opt in Object.values(IP_INFO_API)"
          :key="opt"
          :value="opt"
        >
          {{ opt }}
        </option>
      </select>
    </div>
    <div class="setting-item md:hidden!">
      <div class="setting-item-label">
        {{ $t('scrollAnimationEffect') }}
      </div>
      <input
        type="checkbox"
        v-model="scrollAnimationEffect"
        class="toggle"
      />
    </div>
    <div class="setting-item md:hidden!">
      <div class="setting-item-label">
        {{ $t('swipeInPages') }}
      </div>
      <input
        type="checkbox"
        v-model="swipeInPages"
        class="toggle"
      />
    </div>
    <div
      v-if="swipeInPages"
      class="setting-item md:hidden!"
    >
      <div class="setting-item-label">
        {{ $t('swipeInTabs') }}
      </div>
      <input
        type="checkbox"
        v-model="swipeInTabs"
        class="toggle"
      />
    </div>
    <div class="setting-item md:hidden!">
      <div class="setting-item-label">
        {{ $t('disablePullToRefresh') }}
        <QuestionMarkCircleIcon
          class="h-4 w-4 cursor-pointer"
          @mouseenter="showTip($event, $t('disablePullToRefreshTip'))"
        />
      </div>
      <input
        type="checkbox"
        v-model="disablePullToRefresh"
        class="toggle"
      />
    </div>
    <KeyboardShortcutsSettings v-if="!isMiddleScreen" />
    <div
      v-if="isSingBox"
      class="setting-item"
    >
      <div class="setting-item-label">
        {{ $t('displayAllFeatures') }}
        <QuestionMarkCircleIcon
          class="h-4 w-4 cursor-pointer"
          @mouseenter="showTip($event, $t('displayAllFeaturesTip'))"
        />
      </div>
      <input
        type="checkbox"
        v-model="displayAllFeatures"
        class="toggle"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { isSingBox } from '@/api'
import KeyboardShortcutsSettings from '@/components/settings/general/KeyboardShortcutsSettings.vue'
import LanguageSelect from '@/components/settings/general/LanguageSelect.vue'
import { IP_INFO_API } from '@/constant'
import { useTooltip } from '@/helper/tooltip'
import { isMiddleScreen } from '@/helper/utils'
import {
  autoDisconnectIdleUDP,
  autoDisconnectIdleUDPTime,
  disablePullToRefresh,
  displayAllFeatures,
  IPInfoAPI,
  scrollAnimationEffect,
  swipeInPages,
  swipeInTabs,
} from '@/store/settings'
import { QuestionMarkCircleIcon } from '@heroicons/vue/24/outline'

const { showTip } = useTooltip()
</script>
