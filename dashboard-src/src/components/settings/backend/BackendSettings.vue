<template>
  <!-- backend -->
  <div
    class="text-sm"
    :class="embedded ? '' : 'rounded-lg p-2'"
  >
    <div :class="embedded ? 'core-layout' : 'grid items-stretch gap-3 lg:grid-cols-2 lg:gap-8'">
      <div
        class="min-w-0"
        :class="embedded ? '' : 'rounded-lg p-2'"
      >
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
              role="status"
              :aria-label="$t('desktop.configLoading')"
            />
            <div
              v-else-if="typeof tunState.enabled === 'boolean'"
              class="flex min-w-0 flex-wrap items-center justify-end gap-2"
            >
              <span
                v-if="!tunState.writable"
                class="text-base-content/60 text-xs"
              >
                {{
                  $t(
                    tunState.source === 'host'
                      ? 'desktop.tunReadOnlyHost'
                      : 'desktop.tunReadOnlyApi',
                  )
                }}
              </span>
              <input
                class="toggle shrink-0"
                type="checkbox"
                :aria-label="$t('tunMode')"
                :checked="tunState.enabled"
                :disabled="!tunState.writable"
                @change="handlerTunModeChange"
              />
            </div>
            <span
              v-else
              class="text-sm"
              :class="configStatus === 'error' ? 'text-error' : 'text-base-content/60'"
              role="status"
            >
              {{ $t(unknownStateLabel) }}
            </span>
          </div>
          <div class="setting-item">
            <div class="setting-item-label">
              {{ $t('allowLan') }}
            </div>
            <span
              v-if="configStatus === 'loading'"
              class="loading loading-spinner loading-xs"
              role="status"
              :aria-label="$t('desktop.configLoading')"
            />
            <input
              v-else-if="typeof allowLanEnabled === 'boolean'"
              class="toggle"
              type="checkbox"
              :aria-label="$t('allowLan')"
              :checked="allowLanEnabled"
              @change="handlerAllowLanChange"
            />
            <span
              v-else
              class="text-sm"
              :class="configStatus === 'error' ? 'text-error' : 'text-base-content/60'"
              role="status"
            >
              {{ $t(unknownStateLabel) }}
            </span>
          </div>
        </div>

        <div
          v-if="configStatus === 'error'"
          class="dashboard-note mt-2 flex flex-wrap items-center gap-2"
          role="alert"
        >
          <span class="text-error min-w-0 flex-1 break-words"
            >{{ $t('desktop.configReadFailed') }}: {{ configErrorMessage }}</span
          >
          <button
            class="btn btn-sm dashboard-action-btn"
            type="button"
            @click="retryConfigs"
          >
            {{ $t('desktop.retry') }}
          </button>
        </div>

        <div class="settings-section-label">{{ $t('desktop.actions') }}</div>
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
                {{ $t('desktop.restart') }}
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
                  :disabled="
                    coreHostActions.isCoreUpgrading.value || !coreHostActions.canUpgradeCore.value
                  "
                  @click="coreHostActions.upgradeCore"
                >
                  <span
                    v-if="coreHostActions.isCoreUpgrading.value"
                    class="loading loading-spinner loading-xs"
                  />
                  {{
                    $t(
                      coreHostActions.isCoreUpgrading.value
                        ? 'desktop.upgrading'
                        : 'desktop.upgrade',
                    )
                  }}
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

      <div
        class="flex min-h-0 min-w-0 flex-col"
        :class="embedded ? '' : 'rounded-lg p-2'"
      >
        <div class="dashboard-section-title">{{ $t('desktop.currentDownloads') }}</div>
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
import {
  backendConnectionStatus,
  backendSessionReady,
  captureBackendSession,
} from '@/helper/backendSession'
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
import { hasHostBridge, hostState, postHostMessage } from '@/composables/hostBridge'
import { i18n } from '@/i18n'
import { useBackendRuntimeConfig } from '@/composables/useBackendRuntimeConfig'
import { showNotification } from '@/helper/notification'
import {
  getRequestErrorMessage,
  notifyRequestErrorForSession,
  runManualRequest,
} from '@/helper/requestError'
import { fetchConfigs } from '@/assembly/config'
import { fetchProxies, flushSmartGroupWeightsAPI, hasSmartGroup } from '@/assembly/proxies'
import { fetchRules } from '@/assembly/rules'
import { computed, inject, ref } from 'vue'

defineProps<{ embedded?: boolean }>()

const coreHostActions = inject(coreHostActionsKey, null)
const {
  configStatus,
  configError,
  tunState,
  allowLanEnabled,
  ensureConfigLoaded,
  updateAllowLan,
  updateTunEnabled,
} = useBackendRuntimeConfig()
const unknownStateLabel = computed(() =>
  configStatus.value === 'error'
    ? 'desktop.configReadFailed'
    : configStatus.value === 'idle'
      ? 'desktop.configNotLoaded'
      : 'desktop.configUnknown',
)
const configErrorMessage = computed(() =>
  configError.value
    ? getRequestErrorMessage(configError.value)
    : i18n.global.t(`desktop.connection.${backendConnectionStatus.value}`),
)
const retryConfigs = () => {
  // Authentication/readiness failures stop the session owner and clear its data.
  // Let the host re-probe that session; an isolated successful GET cannot clear it.
  if (hasHostBridge && !backendSessionReady.value) postHostMessage({ type: 'refreshCoreMetadata' })
  else void runManualRequest(ensureConfigLoaded)
}

const reloadAll = () => {
  void Promise.allSettled([fetchConfigs(true), fetchRules(), fetchProxies()])
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

const handlerTunModeChange = async (event: Event) => {
  const input = event.target as HTMLInputElement
  if (!tunState.value.writable) return
  const checked = input.checked
  input.disabled = true
  try {
    await runManualRequest(() => updateTunEnabled(checked))
  } finally {
    input.checked = tunState.value.enabled === true
    input.disabled = !tunState.value.writable
  }
}
const handlerAllowLanChange = async (event: Event) => {
  const input = event.target as HTMLInputElement
  if (typeof allowLanEnabled.value !== 'boolean') return
  const checked = input.checked
  input.disabled = true
  try {
    await runManualRequest(() => updateAllowLan(checked))
  } finally {
    input.checked = allowLanEnabled.value === true
    input.disabled = typeof allowLanEnabled.value !== 'boolean'
  }
}

const handleFlushDNSCache = async () => {
  const session = captureBackendSession()
  try {
    await flushDNSCacheAPI()
  } catch (error) {
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
  try {
    await flushFakeIPAPI()
  } catch (error) {
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
  try {
    await flushSmartGroupWeightsAPI()
  } catch (error) {
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
