<template>
  <!-- backend -->
  <div class="rounded-lg p-2 text-sm">
    <div class="mt-1 mb-3 px-1 text-lg font-semibold">
      <a
        class="inline-flex cursor-pointer items-center gap-2"
        :href="
          isSingBox
            ? 'https://github.com/sagernet/sing-box'
            : MIHOMO_CHANNEL[mihomo?.[0] ?? MIHOMO.Meta].url
        "
        target="_blank"
      >
        {{ $t('backend') }}
        <BackendVersion class="text-sm font-normal" />
      </a>
    </div>

    <div class="grid items-start gap-3 lg:grid-cols-2 lg:gap-12">
      <div>
        <div
          v-if="configs"
          class="settings-grid"
        >
          <div
            v-if="canShowTunMode"
            class="setting-item"
          >
            <div class="setting-item-label">
              {{ $t('tunMode') }}
            </div>
            <input
              class="toggle"
              type="checkbox"
              :checked="tunModeEnabled"
              :disabled="isTunModeReadOnly"
              :class="isTunModeReadOnly && 'opacity-50'"
              @change="hanlderTunModeChange"
            />
          </div>
          <div
            v-if="configs"
            class="setting-item"
          >
            <div class="setting-item-label">
              {{ $t('allowLan') }}
            </div>
            <input
              class="toggle"
              type="checkbox"
              v-model="configs['allow-lan']"
              @change="handlerAllowLanChange"
            />
          </div>
        </div>

        <div class="settings-section-label">操作</div>
        <div class="settings-grid">
          <div class="grid grid-cols-1 gap-2 px-4 py-3 md:grid-cols-2">
            <button
              v-if="coreHostActions"
              class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
              :disabled="!coreHostActions.isRunning.value || coreHostActions.isCoreUpgrading.value"
              @click="coreHostActions.restartCore"
            >
              重启内核
            </button>
            <button
              v-if="coreHostActions?.canUpgradeCore.value"
              class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
              :disabled="coreHostActions.isCoreUpgrading.value"
              @click="coreHostActions.upgradeCore"
            >
              <span
                v-if="coreHostActions.isCoreUpgrading.value"
                class="loading loading-spinner loading-xs"
              />
              {{ coreHostActions.isCoreUpgrading.value ? '升级中' : '升级内核' }}
            </button>
            <button
              class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
              @click="handlerClickReloadConfigs"
            >
              <span
                v-if="isConfigReloading"
                class="loading loading-spinner loading-md"
              ></span>
              {{ $t('reloadConfigs') }}
            </button>
            <template v-if="!isSingBox || displayAllFeatures">
              <button
                class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
                @click="handlerClickUpdateGeo"
              >
                <span
                  v-if="isGeoUpdating"
                  class="loading loading-spinner loading-md"
                ></span>
                {{ $t('updateGeoDatabase') }}
              </button>
            </template>
            <button
              class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
              @click="handleFlushDNSCache"
            >
              {{ $t('flushDNSCache') }}
            </button>
            <button
              class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
              @click="handleFlushFakeIP"
            >
              {{ $t('flushFakeIP') }}
            </button>
            <button
              v-if="hasSmartGroup"
              class="btn btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
              @click="handleFlushSmartWeights"
            >
              {{ $t('flushSmartWeights') }}
            </button>
          </div>
        </div>
      </div>

      <div>
        <div
          v-if="configs && canShowPortsGrid"
          class="settings-grid"
        >
          <BackendPortsGrid />
        </div>

        <div class="settings-section-label">当前下载</div>
        <div class="settings-grid">
          <div class="min-h-[136px] p-3">
            <TopDownloadConnections />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import {
  flushDNSCacheAPI,
  flushFakeIPAPI,
  flushSmartGroupWeightsAPI,
  isSingBox,
  mihomo,
  reloadConfigsAPI,
  updateGeoDataAPI,
} from '@/api'
import BackendVersion from '@/components/common/BackendVersion.vue'
import BackendPortsGrid from '@/components/settings/backend/BackendPortsGrid.vue'
import TopDownloadConnections from '@/components/settings/backend/TopDownloadConnections.vue'
import { coreHostActionsKey } from '@/composables/coreHostActions'
import { MIHOMO, MIHOMO_CHANNEL } from '@/constant'
import { showNotification } from '@/helper/notification'
import { configs, fetchConfigs, updateConfigs } from '@/store/config'
import { fetchProxies, hasSmartGroup } from '@/store/proxies'
import { fetchRules } from '@/store/rules'
import { displayAllFeatures } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import { computed, inject, ref } from 'vue'

const coreHostActions = inject(coreHostActionsKey, null)
const hasWritableTunMode = computed(() => !!configs.value?.tun && !activeBackend.value?.disableTunMode)
const hasReadOnlyTunMode = computed(() => typeof activeBackend.value?.readOnlyTunEnabled === 'boolean')
const canShowTunMode = computed(() => hasWritableTunMode.value || hasReadOnlyTunMode.value)
const isTunModeReadOnly = computed(() => !hasWritableTunMode.value && hasReadOnlyTunMode.value)
const tunModeEnabled = computed(() =>
  hasWritableTunMode.value
    ? !!configs.value?.tun?.enable
    : !!activeBackend.value?.readOnlyTunEnabled,
)
type PortConfigKey = 'mixed-port' | 'port' | 'socks-port' | 'redir-port' | 'tproxy-port'
const portConfigKeys: PortConfigKey[] = [
  'mixed-port',
  'port',
  'socks-port',
  'redir-port',
  'tproxy-port',
]
const canShowPortsGrid = computed(() =>
  portConfigKeys.some((key) => typeof configs.value?.[key] === 'number'),
)

const reloadAll = () => {
  fetchConfigs()
  fetchRules()
  fetchProxies()
}

const isConfigReloading = ref(false)
const handlerClickReloadConfigs = async () => {
  if (isConfigReloading.value) return
  isConfigReloading.value = true
  try {
    await reloadConfigsAPI()
    reloadAll()
    isConfigReloading.value = false
    showNotification({
      content: 'reloadConfigsSuccess',
      type: 'alert-success',
    })
  } catch {
    isConfigReloading.value = false
  }
}

const isGeoUpdating = ref(false)
const handlerClickUpdateGeo = async () => {
  if (isGeoUpdating.value) return
  isGeoUpdating.value = true
  try {
    await updateGeoDataAPI()
    reloadAll()
    isGeoUpdating.value = false
    showNotification({
      content: 'updateGeoSuccess',
      type: 'alert-success',
    })
  } catch {
    isGeoUpdating.value = false
  }
}

const hanlderTunModeChange = async () => {
  if (!hasWritableTunMode.value) {
    return
  }

  await updateConfigs({ tun: { enable: !configs.value?.tun?.enable } })
}
const handlerAllowLanChange = async () => {
  await updateConfigs({ ['allow-lan']: configs.value?.['allow-lan'] })
}

const handleFlushDNSCache = async () => {
  await flushDNSCacheAPI()
  showNotification({
    content: 'flushDNSCacheSuccess',
    type: 'alert-success',
  })
}

const handleFlushFakeIP = async () => {
  await flushFakeIPAPI()
  showNotification({
    content: 'flushFakeIPSuccess',
    type: 'alert-success',
  })
}

const handleFlushSmartWeights = async () => {
  await flushSmartGroupWeightsAPI()
  showNotification({
    content: 'flushSmartWeightsSuccess',
    type: 'alert-success',
  })
}
</script>
