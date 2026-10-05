<template>
  <div class="settings-section-label">
    {{ $t('general') }}
  </div>
  <div class="settings-grid">
    <SettingItem>
      <div class="setting-item-label">
        {{ $t('dashboardSettings') }}
      </div>
      <DashboardSettings icon-only />
    </SettingItem>
    <LanguageSelect />
    <SettingItem>
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
    </SettingItem>
    <SettingItem :when="autoDisconnectIdleUDP">
      <div class="setting-item-label">
        {{ $t('autoDisconnectIdleUDPTime') }}
      </div>
      <input
        type="number"
        class="input input-sm w-20"
        v-model="autoDisconnectIdleUDPTime"
      />
      mins
    </SettingItem>
    <SettingItem>
      <div class="setting-item-label">
        {{ $t('IPInfoAPI') }}
        <QuestionMarkCircleIcon
          class="h-4 w-4 cursor-pointer"
          @mouseenter="showTip($event, $t('IPInfoAPITip'))"
        />
      </div>
      <SelectInput
        class="select select-sm min-w-24"
        v-model="IPInfoAPI"
        :options="Object.values(IP_INFO_API).map((value) => ({ value, label: value }))"
      />
    </SettingItem>
    <SettingItem>
      <div class="setting-item-label">
        {{ $t('geoipCountryDatabaseURL') }}
        <QuestionMarkCircleIcon
          class="h-4 w-4 cursor-pointer"
          @mouseenter="showTip($event, $t('geoipDatabaseURLTip'))"
        />
      </div>
      <TextInput
        class="flex-2"
        v-model="geoipCountryDatabaseURL"
        :clearable="true"
      />
    </SettingItem>
    <SettingItem>
      <div class="setting-item-label">
        {{ $t('geoipASNDatabaseURL') }}
        <QuestionMarkCircleIcon
          class="h-4 w-4 cursor-pointer"
          @mouseenter="showTip($event, $t('geoipDatabaseURLTip'))"
        />
      </div>
      <TextInput
        class="flex-2"
        v-model="geoipASNDatabaseURL"
        :clearable="true"
      />
    </SettingItem>
    <SettingItem class="md:hidden!">
      <div class="setting-item-label">
        {{ $t('scrollAnimationEffect') }}
      </div>
      <input
        type="checkbox"
        v-model="scrollAnimationEffect"
        class="toggle"
      />
    </SettingItem>
    <SettingItem class="md:hidden!">
      <div class="setting-item-label">
        {{ $t('swipeInPages') }}
      </div>
      <input
        type="checkbox"
        v-model="swipeInPages"
        class="toggle"
      />
    </SettingItem>
    <SettingItem
      :when="swipeInPages"
      class="md:hidden!"
    >
      <div class="setting-item-label">
        {{ $t('swipeInTabs') }}
      </div>
      <input
        type="checkbox"
        v-model="swipeInTabs"
        class="toggle"
      />
    </SettingItem>
    <SettingItem class="md:hidden!">
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
    </SettingItem>
    <KeyboardShortcutsSettings />
  </div>
</template>

<script setup lang="ts">
import DashboardSettings from '@/components/common/DashboardSettings.vue'
import SelectInput from '@/components/common/SelectInput.vue'
import TextInput from '@/components/common/TextInput.vue'
import KeyboardShortcutsSettings from '@/components/settings/general/KeyboardShortcutsSettings.vue'
import LanguageSelect from '@/components/settings/general/LanguageSelect.vue'
import SettingItem from '@/components/settings/SettingItem.vue'
import { IP_INFO_API } from '@/constant'
import { useTooltip } from '@/helper/tooltip'
import {
  autoDisconnectIdleUDP,
  autoDisconnectIdleUDPTime,
  disablePullToRefresh,
  geoipASNDatabaseURL,
  geoipCountryDatabaseURL,
  IPInfoAPI,
  scrollAnimationEffect,
  swipeInPages,
  swipeInTabs,
} from '@/store/settings'
import { QuestionMarkCircleIcon } from '@heroicons/vue/24/outline'

const { showTip } = useTooltip()
</script>
