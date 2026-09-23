<template>
  <!-- backend -->
  <div class="rounded-lg p-2 text-sm">
    <div class="grid items-stretch gap-3 lg:grid-cols-2 lg:gap-8">
      <div class="rounded-lg p-2">
        <div class="dashboard-section-title">
          <span class="indicator">
            <span
              v-if="hostState.coreUpdateAvailable"
              class="indicator-item top-1 -right-1 flex"
            >
              <span class="bg-secondary absolute h-2 w-2 animate-ping rounded-full"></span>
              <span class="bg-secondary h-2 w-2 rounded-full"></span>
            </span>
            <span class="inline-flex items-center gap-2">
              {{ $t('backend') }}
              <BackendVersion class="text-sm font-normal" />
            </span>
          </span>
        </div>
        <div class="settings-grid">
          <div
            v-if="tunState.visible"
            class="setting-item"
          >
            <div class="setting-item-label">
              {{ $t('tunMode') }}
            </div>
            <span
              v-if="tunState.loading"
              class="loading loading-spinner loading-xs"
            />
            <input
              v-else
              class="toggle"
              type="checkbox"
              :checked="!!tunState.enabled"
              :disabled="!tunState.writable"
              @change="hanlderTunModeChange"
            />
          </div>
          <div class="setting-item">
            <div class="setting-item-label">
              {{ $t('allowLan') }}
            </div>
            <span
              v-if="!isActiveConfigLoaded"
              class="loading loading-spinner loading-xs"
            />
            <input
              v-else
              class="toggle"
              type="checkbox"
              :checked="!!configs['allow-lan']"
              @change="handlerAllowLanChange"
            />
          </div>
        </div>

        <div class="settings-section-label">操作</div>
        <div class="settings-grid">
          <div class="setting-panel-row">
            <div class="grid grid-cols-1 gap-2 md:grid-cols-2">
              <button
                class="btn btn-sm dashboard-action-btn"
                @click="handlerClickReloadConfigs"
              >
                <span
                  v-if="isConfigReloading"
                  class="loading loading-spinner loading-md"
                ></span>
                {{ $t('reloadConfigs') }}
              </button>
              <button
                v-if="coreHostActions"
                class="btn btn-sm dashboard-action-btn"
                :disabled="
                  !coreHostActions.isRunning.value || coreHostActions.isCoreUpgrading.value
                "
                @click="coreHostActions.restartCore"
              >
                重启内核
              </button>
              <button
                class="btn btn-sm dashboard-action-btn"
                @click="handleFlushDNSCache"
              >
                {{ $t('flushDNSCache') }}
              </button>
              <button
                class="btn btn-sm dashboard-action-btn"
                @click="handleFlushFakeIP"
              >
                {{ $t('flushFakeIP') }}
              </button>
              <template v-if="!isSingBox">
                <button
                  class="btn btn-sm dashboard-action-btn"
                  @click="handlerClickUpdateGeo"
                >
                  <span
                    v-if="isGeoUpdating"
                    class="loading loading-spinner loading-md"
                  ></span>
                  {{ $t('updateGeoDatabase') }}
                </button>
              </template>
              <span
                v-if="coreHostActions?.canUpgradeCore.value"
                class="indicator w-full"
              >
                <span
                  v-if="hostState.coreUpdateAvailable"
                  class="indicator-item top-1 -right-1 flex"
                >
                  <span class="bg-secondary absolute h-2 w-2 animate-ping rounded-full"></span>
                  <span class="bg-secondary h-2 w-2 rounded-full"></span>
                </span>
                <button
                  class="btn btn-sm dashboard-action-btn w-full"
                  :disabled="coreHostActions.isCoreUpgrading.value"
                  @click="coreHostActions.upgradeCore"
                >
                  <span
                    v-if="coreHostActions.isCoreUpgrading.value"
                    class="loading loading-spinner loading-xs"
                  />
                  {{ coreHostActions.isCoreUpgrading.value ? '升级中' : '升级内核' }}
                </button>
              </span>
              <button
                v-if="hasSmartGroup"
                class="btn btn-sm dashboard-action-btn"
                @click="handleFlushSmartWeights"
              >
                {{ $t('flushSmartWeights') }}
              </button>
            </div>
          </div>
        </div>
      </div>

      <div class="flex min-h-0 flex-col rounded-lg p-2">
        <div class="dashboard-section-title">当前下载</div>
        <div class="settings-grid min-h-[232px] flex-1">
          <div class="setting-panel-row h-full">
            <TopDownloadConnections />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { captureBackendSession } from '@/helper/backendSession'
import {
  flushDNSCacheAPI,
  flushFakeIPAPI,
  reloadConfigsAPI,
  updateGeoDataAPI,
} from '@/assembly/config'
import { isSingBoxCore as isSingBox } from '@/assembly/version'
import BackendVersion from '@/components/common/BackendVersion.vue'
import TopDownloadConnections from '@/components/settings/backend/TopDownloadConnections.vue'
import { coreHostActionsKey } from '@/composables/coreHostActions'
import { hostState } from '@/composables/hostBridge'
import { useBackendRuntimeConfig } from '@/composables/useBackendRuntimeConfig'
import { showNotification } from '@/helper/notification'
import { notifyRequestErrorForSession, runManualRequest } from '@/helper/requestError'
import { fetchConfigs } from '@/assembly/config'
import { fetchProxies, flushSmartGroupWeightsAPI, hasSmartGroup } from '@/assembly/proxies'
import { fetchRules } from '@/assembly/rules'
import { inject, ref } from 'vue'

const coreHostActions = inject(coreHostActionsKey, null)
const { configs, isActiveConfigLoaded, tunState, updateAllowLan, updateTunEnabled } =
  useBackendRuntimeConfig()

const reloadAll = () => {
  void Promise.allSettled([fetchConfigs(), fetchRules(), fetchProxies()])
}

const isConfigReloading = ref(false)
const handlerClickReloadConfigs = async () => {
  if (isConfigReloading.value) return
  isConfigReloading.value = true
  const session = captureBackendSession()
  try {
    await reloadConfigsAPI()
    if (!session.isCurrent()) return
    reloadAll()
    isConfigReloading.value = false
    showNotification({
      content: 'reloadConfigsSuccess',
      type: 'alert-success',
    })
  } catch (error) {
    notifyRequestErrorForSession(error, session)
  } finally {
    isConfigReloading.value = false
  }
}

const isGeoUpdating = ref(false)
const handlerClickUpdateGeo = async () => {
  if (isGeoUpdating.value) return
  isGeoUpdating.value = true
  const session = captureBackendSession()
  try {
    await updateGeoDataAPI()
    if (!session.isCurrent()) return
    reloadAll()
    isGeoUpdating.value = false
    showNotification({
      content: 'updateGeoSuccess',
      type: 'alert-success',
    })
  } catch (error) {
    notifyRequestErrorForSession(error, session)
  } finally {
    isGeoUpdating.value = false
  }
}

const hanlderTunModeChange = async () => {
  await runManualRequest(() => updateTunEnabled(!configs.value?.tun?.enable))
}
const handlerAllowLanChange = async (event: Event) => {
  if (!isActiveConfigLoaded.value) return
  const checked =
    event.target instanceof HTMLInputElement ? event.target.checked : !!configs.value?.['allow-lan']
  await runManualRequest(() => updateAllowLan(checked))
}

const handleFlushDNSCache = async () => {
  const session = captureBackendSession()
  try { await flushDNSCacheAPI() } catch (error) {
    notifyRequestErrorForSession(error, session)
    return
  }
  if (!session.isCurrent()) return
  showNotification({
    content: 'flushDNSCacheSuccess',
    type: 'alert-success',
  })
}

const handleFlushFakeIP = async () => {
  const session = captureBackendSession()
  try { await flushFakeIPAPI() } catch (error) {
    notifyRequestErrorForSession(error, session)
    return
  }
  if (!session.isCurrent()) return
  showNotification({
    content: 'flushFakeIPSuccess',
    type: 'alert-success',
  })
}

const handleFlushSmartWeights = async () => {
  const session = captureBackendSession()
  try { await flushSmartGroupWeightsAPI() } catch (error) {
    notifyRequestErrorForSession(error, session)
    return
  }
  if (!session.isCurrent()) return
  showNotification({
    content: 'flushSmartWeightsSuccess',
    type: 'alert-success',
  })
}
</script>
