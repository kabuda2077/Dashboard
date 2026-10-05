<template>
  <div class="h-full overflow-x-hidden overflow-y-auto">
    <CtrlsBar page-aligned>
      <div class="core-toolbar core-runtime-toolbar">
        <span
          class="core-runtime-dot"
          :class="hostState.isRunning && backendConnectionStatus === 'ready' ? 'bg-success' : 'bg-warning'"
          role="img"
          :aria-label="runtimeStatusText"
          :title="runtimeStatusText"
        />
        <div
          class="core-status-box"
          :title="runtimeStatusText"
        >
          <span class="min-w-0 truncate font-semibold">{{
            hostState.coreTitle || 'Dashboard'
          }}</span>
          <span
            v-if="hostState.isRunning && hostState.processId"
            class="core-status-pid text-base-content/60 min-w-0 truncate text-xs"
          >
            PID {{ hostState.processId }}
          </span>
        </div>
        <div class="core-runtime-actions">
          <button
            class="core-top-button btn-primary"
            :disabled="busy || !hasHostBridge || setupVisible"
            @click="openSwitch"
          >
            <ArrowsRightLeftIcon class="h-3.5 w-3.5" />{{ t('desktop.switch') }}
          </button>
          <button
            class="core-top-button btn-success"
            :disabled="busy || !hasHostBridge || hostState.isRunning || setupVisible"
            @click="run('start')"
          >
            <PlayIcon class="h-3.5 w-3.5" />{{ t('desktop.start') }}
          </button>
          <button
            class="core-top-button btn-warning"
            :disabled="busy || !hostState.isRunning || setupVisible"
            @click="run('stop')"
          >
            <StopIcon class="h-3.5 w-3.5" />{{ t('desktop.stop') }}
          </button>
        </div>
      </div>
    </CtrlsBar>
    <div
      class="core-content mx-auto w-full max-w-7xl p-3"
      :style="{ paddingBottom: padding.paddingBottom }"
    >
      <div
        v-if="!hasHostBridge"
        class="dashboard-note mb-3"
      >
        {{ t('desktop.hostRequired') }}
      </div>

      <div
        v-if="draft && !setupVisible && !switchContext"
        class="core-layout"
      >
        <section class="min-w-0">
          <h2 class="dashboard-section-title">{{ t('desktop.profile') }}</h2>
          <div class="settings-grid">
            <CoreProfileFields
              :draft="draft"
              id-prefix="core"
              :busy="busy"
              @choose="chooseFile"
              @location="openLocation"
              @retry="postHostMessage({ type: 'refreshCoreMetadata' })"
            >
              <template #secret-action
                ><button
                  data-testid="save-core-profile"
                  class="btn btn-primary btn-sm"
                  :disabled="busy || !!draft.remote || !dirty"
                  @click="run('saveProfile')"
                >
                  {{ t('save') }}
                </button></template
              >
            </CoreProfileFields>
            <div
              v-if="dirty || draft.remote || hostState.requiresRestart"
              class="setting-panel-row core-profile-actions"
            >
              <span
                v-if="dirty"
                class="text-base-content/60 text-sm"
                >{{ t('desktop.dirty') }}</span
              >
              <button
                v-if="dirty || draft.remote"
                data-testid="discard-core-profile"
                class="btn btn-warning btn-sm"
                :disabled="busy"
                @click="reloadDraft"
              >
                {{ t(draft.remote ? 'desktop.reload' : 'desktop.discard') }}
              </button>
              <span
                v-if="hostState.requiresRestart"
                class="text-warning text-sm"
                >{{ t('desktop.requiresRestart') }}</span
              >
            </div>
          </div>
          <h2 class="dashboard-section-title">{{ t('desktop.desktopOptions') }}</h2>
          <div class="settings-grid">
            <label
              v-for="option in desktopOptions"
              :key="option.key"
              class="setting-item"
            >
              <span class="setting-item-label">{{ t(`desktop.${option.key}`) }}</span>
              <input
                type="checkbox"
                class="toggle"
                :checked="hostState.desktopOptions?.[option.key]"
                :disabled="option.key === 'autostart' && hostState.isAutostartUpdating"
                @change="setOption(option.key, $event)"
              />
            </label>
          </div>
        </section>
        <section class="core-output-section flex min-h-0 min-w-0 flex-col">
          <h2 class="dashboard-section-title">{{ t('desktop.output') }}</h2>
          <p
            v-if="hostState.logWriteFailures || hostState.droppedLogEntries"
            class="text-warning mb-2 text-xs"
          >
            {{
              t('desktop.logFailures', {
                failed: hostState.logWriteFailures || 0,
                dropped: hostState.droppedLogEntries || 0,
              })
            }}
          </p>
          <div class="core-output-panel settings-grid">
            <pre
              class="dashboard-log-block core-output-log"
              aria-live="off"
              >{{ hostState.logText || t('desktop.noOutput') }}</pre>
          </div>
        </section>
      </div>
      <SettingsContent
        embedded
        id-prefix="core-settings"
        :scroll-to="scrollTo"
        class="mt-4"
      />
    </div>
    <dialog
      v-if="draft && (setupVisible || switchContext)"
      ref="profileDialog"
      class="modal modal-open"
      role="dialog"
      aria-modal="true"
      aria-labelledby="core-dialog-title"
      :data-testid="setupVisible ? 'setup-guide' : 'switch-core-dialog'"
      @cancel.prevent="reloadConfirmation ? cancelReload() : switchContext && cancelSwitch()"
    >
      <div class="modal-box core-profile-dialog">
        <h2
          id="core-dialog-title"
          class="text-lg font-semibold"
        >
          {{ t(setupVisible ? 'desktop.setupTitle' : 'desktop.switchTitle') }}
        </h2>
        <div
          v-if="reloadConfirmation"
          data-testid="reload-draft-confirmation"
          class="mt-4"
        >
          <p>{{ t('desktop.reloadMessage') }}</p>
          <div class="modal-action">
            <button
              ref="reloadCancelButton"
              class="btn btn-sm"
              @click="cancelReload"
            >
              {{ t('cancel') }}
            </button>
            <button
              class="btn btn-sm btn-warning"
              :disabled="busy"
              @click="confirmReload"
            >
              {{ t('confirm') }}
            </button>
          </div>
        </div>
        <template v-else>
          <div
            v-if="setupVisible"
            class="my-4 grid grid-cols-2 gap-2"
            role="group"
            :aria-label="t('desktop.editProfile')"
          >
            <button
              v-for="core in ['mihomo', 'sing-box'] as const"
              :key="core"
              class="btn btn-sm"
              :class="kind === core ? 'btn-primary' : 'dashboard-action-btn'"
              :aria-pressed="kind === core"
              :disabled="busy"
              @click="selectedKind = core"
            >
              {{ core }}
            </button>
          </div>
          <p
            v-else
            class="text-base-content/60 my-3 text-sm"
          >
            {{ t('desktop.switchTarget', { core: kind }) }}
          </p>
          <div class="settings-grid my-4">
            <CoreProfileFields
              :draft="draft"
              id-prefix="core"
              :busy="busy"
              compact
              @choose="chooseFile"
            />
          </div>
          <p class="dashboard-note">
            {{ t(kind === 'mihomo' ? 'desktop.mihomoSetupHelp' : 'desktop.singBoxSetupHelp') }}
          </p>
          <p
            v-if="setupVisible && hostState.isRunning"
            class="text-base-content/60 mt-2 text-sm"
            role="status"
          >
            {{ t(`desktop.api.${backendConnectionStatus}`) }}
          </p>
          <p
            v-if="dialogNotice"
            class="mt-2 text-sm"
            :class="{
              'text-error': dialogNotice.severity === 'error',
              'text-warning': dialogNotice.severity === 'warning',
              'text-success': dialogNotice.severity === 'success',
            }"
            role="status"
            data-testid="core-dialog-notice"
          >
            {{ dialogNotice.content }}
          </p>
          <p
            v-if="draft.remote"
            class="text-warning mt-2 text-sm"
          >
            {{ t('desktop.reload') }}
          </p>
          <div class="modal-action flex-wrap items-center justify-between">
            <span class="text-base-content/60 text-sm">{{
              t(
                setupVisible
                  ? 'desktop.setupReadyHelp'
                  : dirty
                    ? 'desktop.switchDraftMessage'
                    : 'desktop.switchMessage',
                { core: kind },
              )
            }}</span>
            <div class="flex gap-2">
              <button
                v-if="switchContext"
                class="btn btn-sm dashboard-action-btn"
                :disabled="pending"
                @click="cancelSwitch"
              >
                {{ t('cancel') }}
              </button>
              <button
                v-if="draft.remote"
                data-testid="reload-dialog-draft"
                class="btn btn-sm btn-warning"
                :disabled="busy"
                @click="reloadDraft"
              >
                {{ t('desktop.reload') }}
              </button>
              <button
                class="btn btn-sm btn-primary"
                :disabled="busy || !!draft.remote"
                @click="setupVisible ? startSetup() : confirmSwitch()"
              >
                {{
                  t(
                    setupVisible
                      ? setupReady
                        ? 'desktop.finishSetup'
                        : 'desktop.startCore'
                      : dirty
                        ? 'desktop.saveAndSwitch'
                        : 'desktop.switch',
                  )
                }}
              </button>
            </div>
          </div>
        </template>
      </div>
    </dialog>
  </div>
</template>

<script setup lang="ts">
import CtrlsBar from '@/components/common/CtrlsBar.vue'
import CoreProfileFields from '@/components/settings/CoreProfileFields.vue'
import { ArrowsRightLeftIcon, PlayIcon, StopIcon } from '@heroicons/vue/24/outline'
import SettingsContent from '@/components/settings/SettingsContent.vue'
import { coreHostActionsKey } from '@/composables/coreHostActions'
import {
  commandHost,
  hasHostBridge,
  hostState,
  postHostMessage,
  type CoreKind,
  type DesktopOptions,
  type HostCommand,
  type CommandResult,
} from '@/composables/hostBridge'
import { usePaddingForViews } from '@/composables/paddingViews'
import { showConfirmDialog } from '@/helper/confirmDialog'
import {
  acceptCoreDraft,
  coreDrafts,
  isDraftDirty,
  resetCoreDraft,
  type CoreDraft,
  syncCoreDrafts,
  toProfileEdit,
} from '@/helper/hostDraft'
import { showHostNotice as notifyHostResult } from '@/helper/hostNotice'
import { backendConnectionStatus } from '@/helper/backendSession'
import { computed, nextTick, onMounted, onUnmounted, provide, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { i18n } from '@/i18n'
const t = i18n.global.t

let interactiveFrame = 0
onMounted(() => {
  interactiveFrame = requestAnimationFrame(() => {
    performance.mark('core-interactive')
    postHostMessage({
      type: 'performance',
      name: 'coreInteractiveFromNavigation',
      durationMs: Math.round(performance.now()),
    })
  })
})
onUnmounted(() => cancelAnimationFrame(interactiveFrame))
const route = useRoute()
const scrollTo = computed(() =>
  typeof route.query.scrollTo === 'string' ? route.query.scrollTo : null,
)
const { padding } = usePaddingForViews()
const selectedKind = ref<CoreKind>(hostState.value.coreType ?? 'mihomo')
const kind = computed<CoreKind>(() => selectedKind.value)
const setupVisible = computed(() => hasHostBridge && !hostState.value.setupCompleted)
const setupReady = computed(
  () =>
    hostState.value.isRunning &&
    hostState.value.coreType === kind.value &&
    backendConnectionStatus.value === 'ready' &&
    !dirty.value,
)
const runtimeStatusText = computed(() =>
  [
    hostState.value.coreTitle || 'Dashboard',
    t(hostState.value.isRunning ? 'desktop.running' : 'desktop.stopped'),
    ...(hostState.value.isRunning && hostState.value.processId
      ? [`PID ${hostState.value.processId}`]
      : []),
    t(`desktop.connection.${backendConnectionStatus.value}`),
  ].join(' · '),
)
const setupStartedKind = ref<CoreKind>()
type SwitchContext = { active: CoreKind; target: CoreKind; revision: number; epoch: number }
const switchContext = ref<SwitchContext>()
const dialogNotice = ref<{ content: string; severity: 'info' | 'warning' | 'error' | 'success' }>()
const showHostNotice = (
  message: string,
  severity: 'info' | 'warning' | 'error' | 'success' = 'info',
) => {
  if (setupVisible.value || switchContext.value) {
    const key = `desktop.result.${message}`
    dialogNotice.value = { content: i18n.global.te(key) ? t(key) : message, severity }
  }
  notifyHostResult(message, severity)
}
type ReloadContext = { target: CoreKind; entry: CoreDraft; revision: number; values: string }
const reloadConfirmation = ref<ReloadContext>()
const reloadCancelButton = ref<HTMLButtonElement>()
watch(reloadCancelButton, (button) => button?.focus())
const cancelReload = async () => {
  reloadConfirmation.value = undefined
  await nextTick()
  profileDialog.value
    ?.querySelector<HTMLButtonElement>('[data-testid=reload-dialog-draft]')
    ?.focus()
}
const profileDialog = ref<HTMLDialogElement>()
watch(
  profileDialog,
  async (dialog) => {
    if (!dialog) {
      reloadConfirmation.value = undefined
      return
    }
    await nextTick()
    if (!dialog.isConnected || dialog.open) return
    if (typeof dialog.showModal === 'function') dialog.showModal()
    else dialog.setAttribute('open', '')
  },
  { immediate: true },
)
watch(
  () => hostState.value.coreType,
  (value) => {
    if (value && !setupVisible.value && !switchContext.value) selectedKind.value = value
  },
)
const draft = computed(() => coreDrafts[kind.value])
const dirty = computed(() => !!draft.value && isDraftDirty(draft.value))
const pending = ref(false)
const busy = computed(
  () =>
    pending.value ||
    (hostState.value.operation !== undefined && hostState.value.operation !== 'idle'),
)
watch(
  () => hostState.value.profiles,
  (profiles) => {
    if (profiles) syncCoreDrafts(profiles)
  },
  { immediate: true, flush: 'sync' },
)

const desktopOptions: { key: keyof DesktopOptions }[] = [
  { key: 'startCoreOnLaunch' },
  { key: 'minimizeToTray' },
  { key: 'lightweightMode' },
  { key: 'autostart' },
]

type UpgradeContext = { kind: CoreKind; revision: number; epoch: number }
const matchesUpgradeContext = (context: UpgradeContext) =>
  hostState.value.coreType === context.kind &&
  hostState.value.profiles?.[context.kind]?.revision === context.revision &&
  hostState.value.runtimeEpoch === context.epoch

const run = async (
  type:
    'saveProfile' | 'start' | 'restart' | 'switchCore' | 'stop' | 'upgradeCore' | 'completeSetup',
  confirmation?: UpgradeContext,
  switchTarget?: CoreKind,
): Promise<CommandResult | undefined> => {
  if (busy.value || !hasHostBridge) return
  dialogNotice.value = undefined
  const target =
    switchTarget ??
    (type === 'stop' || type === 'restart' || type === 'upgradeCore' || type === 'completeSetup'
      ? (hostState.value.coreType ?? kind.value)
      : kind.value)
  const upgradeContext: UpgradeContext = confirmation ?? {
    kind: target,
    revision: hostState.value.profiles?.[target]?.revision ?? -1,
    epoch: hostState.value.runtimeEpoch ?? -1,
  }
  if (confirmation && !matchesUpgradeContext(confirmation)) {
    showHostNotice('operationContextChanged', 'warning')
    return
  }
  const entry = coreDrafts[target]
  const submitted = entry ? { ...entry.values } : undefined
  const includeDraft =
    entry &&
    (type === 'saveProfile' ||
      ((type === 'start' || type === 'restart' || type === 'switchCore') && isDraftDirty(entry)))
  const command: HostCommand = {
    type,
    coreType: target,
    confirmUnverified: !!confirmation,
    ...(type === 'upgradeCore'
      ? { expectedRevision: upgradeContext.revision, expectedRuntimeEpoch: upgradeContext.epoch }
      : {}),
    ...(includeDraft ? { expectedRevision: entry.base.revision, draft: toProfileEdit(entry) } : {}),
  }
  pending.value = true
  let needsConfirmation = false
  let result: CommandResult | undefined
  try {
    result = await commandHost(command)
    const canonical = hostState.value.profiles?.[target]
    if (
      includeDraft &&
      result.saved &&
      entry &&
      submitted &&
      canonical &&
      coreDrafts[target] === entry
    )
      acceptCoreDraft(entry, submitted, canonical)
    needsConfirmation = result.code === 'confirmationRequired'
    if (
      !needsConfirmation &&
      result.status !== 'cancelled' &&
      result.status !== 'elevationRequired'
    )
      showHostNotice(
        result.message ?? result.code,
        result.status === 'completed' ? 'success' : 'error',
      )
  } catch (error) {
    showHostNotice(error instanceof Error ? error.message : String(error), 'error')
  } finally {
    pending.value = false
  }
  if (needsConfirmation) {
    if (!matchesUpgradeContext(upgradeContext)) {
      showHostNotice('operationContextChanged', 'warning')
      return
    }
    const { confirmed } = await showConfirmDialog({
      title: t('desktop.unverifiedTitle'),
      message: t('desktop.unverifiedMessage'),
    })
    if (confirmed) return await run('upgradeCore', upgradeContext)
  }
  return result
}

const chooseFile = async (config: boolean) => {
  const target = kind.value
  const entry = draft.value
  if (!entry) return
  const key = config ? 'configPath' : 'exePath'
  const previous = entry.values[key]
  try {
    const result = await commandHost({
      type: config ? 'chooseConfigFile' : 'chooseCoreFile',
      coreType: target,
    })
    if (
      result.status === 'completed' &&
      result.path &&
      kind.value === target &&
      coreDrafts[target] === entry &&
      entry.values[key] === previous
    )
      entry.values[key] = result.path
  } catch (error) {
    showHostNotice(String(error), 'error')
  }
}
const setOption = async (option: keyof DesktopOptions, event: Event) => {
  const input = event.target as HTMLInputElement
  const value = input.checked
  input.disabled = true
  try {
    const result = await commandHost({ type: 'setDesktopOption', option, value })
    if (result.status !== 'completed') showHostNotice(result.message ?? result.code, 'error')
  } catch (error) {
    showHostNotice(String(error), 'error')
  } finally {
    input.disabled = false
    input.checked = !!hostState.value.desktopOptions?.[option]
  }
}
const openLocation = (config: boolean) =>
  postHostMessage({
    type: config ? 'openConfigLocation' : 'openCoreLocation',
    coreType: kind.value,
  })
const openSwitch = () => {
  if (busy.value || !hasHostBridge || setupVisible.value) return
  dialogNotice.value = undefined
  const active = hostState.value.coreType ?? 'mihomo'
  const target = active === 'mihomo' ? 'sing-box' : 'mihomo'
  switchContext.value = {
    active,
    target,
    revision: hostState.value.profiles?.[target]?.revision ?? -1,
    epoch: hostState.value.runtimeEpoch ?? -1,
  }
  selectedKind.value = target
}
const cancelSwitch = () => {
  if (pending.value) return
  switchContext.value = undefined
  selectedKind.value = hostState.value.coreType ?? 'mihomo'
}
const confirmSwitch = async () => {
  const context = switchContext.value
  if (!context || busy.value) return
  if (
    hostState.value.coreType !== context.active ||
    hostState.value.runtimeEpoch !== context.epoch ||
    hostState.value.profiles?.[context.target]?.revision !== context.revision ||
    kind.value !== context.target ||
    draft.value?.remote
  ) {
    showHostNotice('operationContextChanged', 'warning')
    cancelSwitch()
    return
  }
  const result = await run('switchCore', undefined, context.target)
  if (result?.status === 'completed' || result?.status === 'elevationRequired') cancelSwitch()
}
const startSetup = async () => {
  if (busy.value || !draft.value || draft.value.remote) return
  if (setupReady.value) {
    await run('completeSetup')
    return
  }
  const target = kind.value
  const type = hostState.value.isRunning
    ? hostState.value.coreType === target
      ? 'restart'
      : 'switchCore'
    : 'start'
  const result = await run(type, undefined, target)
  if (result?.status === 'completed') setupStartedKind.value = target
}
watch(
  () => [setupReady.value, busy.value, setupStartedKind.value, setupVisible.value] as const,
  ([ready, working, started, visible]) => {
    if (visible && ready && !working && started === kind.value) {
      setupStartedKind.value = undefined
      void run('completeSetup')
    }
    if (!visible) {
      setupStartedKind.value = undefined
      if (!switchContext.value) selectedKind.value = hostState.value.coreType ?? 'mihomo'
    }
  },
  { flush: 'post' },
)
const applyReload = (context: ReloadContext) => {
  const { target, entry, revision, values } = context
  const profile = hostState.value.profiles?.[target]
  if (
    busy.value ||
    kind.value !== target ||
    coreDrafts[target] !== entry ||
    profile?.revision !== revision ||
    JSON.stringify(entry.values) !== values
  ) {
    showHostNotice('operationContextChanged', 'warning')
    return
  }
  resetCoreDraft(target, profile)
  if (switchContext.value?.target === target) switchContext.value.revision = profile.revision
}
const confirmReload = () => {
  const context = reloadConfirmation.value
  reloadConfirmation.value = undefined
  if (context) applyReload(context)
  void nextTick(() => profileDialog.value?.querySelector<HTMLInputElement>('#core-exe')?.focus())
}
const reloadDraft = async () => {
  const target = kind.value,
    entry = coreDrafts[target],
    profile = hostState.value.profiles?.[target]
  if (!entry || !profile || busy.value) return
  const context = {
    target,
    entry,
    revision: profile.revision,
    values: JSON.stringify(entry.values),
  }
  if (profileDialog.value?.open) {
    // Confirm inside the existing native modal; an external div would be inert beneath it.
    reloadConfirmation.value = context
    return
  }
  if (
    (
      await showConfirmDialog({
        title: t('desktop.reloadTitle'),
        message: t('desktop.reloadMessage'),
      })
    ).confirmed
  )
    applyReload(context)
}
provide(coreHostActionsKey, {
  isRunning: computed(() => !!hostState.value.isRunning),
  isCoreUpgrading: computed(() => !!hostState.value.isCoreUpgrading),
  canUpgradeCore: computed(() => !!hostState.value.canUpgradeCore && !busy.value),
  restartCore: () => {
    void run('restart')
  },
  upgradeCore: () => {
    void run('upgradeCore')
  },
})
</script>
