<template>
  <div class="flex flex-col text-sm">
    <div class="settings-section-label">
      {{ $t('latency') }}
    </div>
    <div class="settings-grid">
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('speedtestMode') }}
          <QuestionMarkCircleIcon
            class="h-4 w-4"
            @mouseenter="speedtestModeTip"
          />
        </div>
        <select
          class="select select-sm min-w-24"
          v-model="speedtestMode"
        >
          <option :value="SPEEDTEST_MODE.DASHBOARD">
            {{ $t('speedtestModeDashboard') }}
          </option>
          <option :value="SPEEDTEST_MODE.CORE">
            {{ $t('speedtestModeCore') }}
          </option>
        </select>
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('speedtestUrl') }}
        </div>
        <TextInput
          class="flex-2"
          v-model="speedtestUrl"
          :clearable="true"
        />
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('speedtestTimeout') }}
        </div>
        <input
          type="number"
          class="input input-sm w-20"
          v-model="speedtestTimeout"
        />
        ms
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('lowLatencyDesc') }}
        </div>
        <input
          type="number"
          class="input input-sm w-20"
          v-model="lowLatency"
        />
        ms
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('mediumLatencyDesc') }}
        </div>
        <input
          type="number"
          class="input input-sm w-20"
          v-model="mediumLatency"
        />
        ms
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('ipv6Test') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="IPv6test"
        />
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('independentLatencyTest') }}
          <QuestionMarkCircleIcon
            class="h-4 w-4"
            @mouseenter="independentLatencyTestTip"
          />
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="independentLatencyTest"
        />
      </div>
      <GroupTestUrlsSettings v-if="independentLatencyTest" />
    </div>
    <div class="settings-section-label">
      {{ $t('appearance') }}
    </div>
    <div class="settings-grid">
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('proxyFolderMode') }}
        </div>
        <select
          class="select select-sm min-w-24"
          v-model="proxyFolderMode"
        >
          <option :value="FOLDER_MODE.AUTO">
            {{ $t('folderModeAuto') }}
          </option>
          <option :value="FOLDER_MODE.ON">
            {{ $t('folderModeOn') }}
          </option>
          <option :value="FOLDER_MODE.OFF">
            {{ $t('folderModeOff') }}
          </option>
        </select>
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('twoColumnProxyGroup') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="twoColumnProxyGroup"
        />
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('truncateProxyName') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="truncateProxyName"
        />
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('displayGlobalByMode') }}
        </div>
        <input
          class="toggle"
          type="checkbox"
          v-model="displayGlobalByMode"
        />
      </div>
      <div
        v-if="displayGlobalByMode && isSingBox"
        class="setting-item"
      >
        <div class="setting-item-label">
          {{ $t('customGlobalNode') }}
        </div>
        <select
          class="select select-sm min-w-24"
          v-model="customGlobalNode"
        >
          <option
            v-for="opt in Object.keys(proxyMap)"
            :key="opt"
            :value="opt"
          >
            {{ opt }}
          </option>
        </select>
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('proxyPreviewType') }}
        </div>
        <select
          class="select select-sm min-w-24"
          v-model="proxyPreviewType"
        >
          <option
            v-for="opt in Object.values(PROXY_PREVIEW_TYPE)"
            :key="opt"
            :value="opt"
          >
            {{ $t(opt) }}
          </option>
        </select>
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('proxyCardSize') }}
        </div>
        <select
          class="select select-sm min-w-24"
          v-model="proxyCardSize"
          @change="handlerProxyCardSizeChange"
        >
          <option
            v-for="opt in Object.values(PROXY_CARD_SIZE)"
            :key="opt"
            :value="opt"
          >
            {{ $t(opt) }}
          </option>
        </select>
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('proxyGroupIconSize') }}
        </div>
        <input
          type="number"
          class="input input-sm w-24"
          v-model="proxyGroupIconSize"
        />
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('proxyGroupIconMargin') }}
        </div>
        <input
          type="number"
          class="input input-sm w-24"
          v-model="proxyGroupIconMargin"
        />
      </div>
      <IconSettings />
    </div>
  </div>
</template>

<script setup lang="ts">
import { isSingBox } from '@/api'
import { FOLDER_MODE, PROXY_CARD_SIZE, PROXY_PREVIEW_TYPE, SPEEDTEST_MODE } from '@/constant'
import { useTooltip } from '@/helper/tooltip'
import { getMinCardWidth } from '@/helper/utils'
import { proxyMap } from '@/store/proxies'
import {
  customGlobalNode,
  displayGlobalByMode,
  independentLatencyTest,
  IPv6test,
  lowLatency,
  mediumLatency,
  minProxyCardWidth,
  proxyCardSize,
  proxyGroupIconMargin,
  proxyGroupIconSize,
  proxyPreviewType,
  speedtestMode,
  speedtestTimeout,
  speedtestUrl,
  truncateProxyName,
  proxyFolderMode,
  twoColumnProxyGroup,
} from '@/store/settings'
import { QuestionMarkCircleIcon } from '@heroicons/vue/24/outline'
import { useI18n } from 'vue-i18n'
import TextInput from '../../common/TextInput.vue'
import GroupTestUrlsSettings from './GroupTestUrlsSettings.vue'
import IconSettings from './IconSettings.vue'

const { showTip } = useTooltip()
const { t } = useI18n()
const speedtestModeTip = (e: Event) => {
  return showTip(e, t('speedtestModeTip'))
}
const independentLatencyTestTip = (e: Event) => {
  return showTip(e, t('independentLatencyTestTip'))
}

const handlerProxyCardSizeChange = () => {
  minProxyCardWidth.value = getMinCardWidth(proxyCardSize.value)
}
</script>
