<template>
  <div class="p-2 sm:p-4">
    <div class="mx-auto grid w-full max-w-5xl grid-cols-1 items-start gap-x-6 lg:grid-cols-2">
      <!-- Network Quality -->
      <section class="flex flex-col gap-1">
        <div class="settings-section-label">{{ $t('networkQuality') }}</div>
        <div class="settings-grid">
          <div class="setting-item">
            <div class="setting-item-label">{{ $t('configURL') }}</div>
            <input
              class="input input-sm w-48 max-w-full sm:w-80"
              v-model="nqConfigURL"
              :disabled="nqRunning"
            />
          </div>
          <div class="setting-item">
            <div class="setting-item-label">{{ $t('outbound') }}</div>
            <select
              class="select select-sm min-w-24"
              v-model="nqOutbound"
              :disabled="nqRunning"
            >
              <option value="">{{ $t('default') }}</option>
              <option
                v-for="tag in outboundTags"
                :key="tag"
                :value="tag"
              >
                {{ tag }}
              </option>
            </select>
          </div>
          <div class="setting-item">
            <div class="setting-item-label">{{ $t('maxRuntime') }}</div>
            <select
              class="select select-sm min-w-24"
              v-model.number="nqMaxRuntime"
              :disabled="nqRunning"
            >
              <option
                v-for="sec in [20, 30, 60]"
                :key="sec"
                :value="sec"
              >
                {{ sec }} s
              </option>
            </select>
          </div>
          <div class="setting-item">
            <div class="setting-item-label">{{ $t('serial') }}</div>
            <input
              type="checkbox"
              class="toggle"
              v-model="nqSerial"
              :disabled="nqRunning"
            />
          </div>
          <div class="setting-item">
            <div class="setting-item-label">{{ $t('http3') }}</div>
            <input
              type="checkbox"
              class="toggle"
              v-model="nqHttp3"
              :disabled="nqRunning"
            />
          </div>
          <div class="setting-item">
            <button
              v-if="!nqRunning"
              class="btn btn-neutral btn-sm ml-auto"
              @click="startNetworkQuality"
            >
              {{ $t('startTest') }}
            </button>
            <button
              v-else
              class="btn btn-sm ml-auto"
              @click="cancelNetworkQuality"
            >
              {{ $t('cancelTest') }}
            </button>
          </div>
        </div>

        <div
          v-if="nqError"
          class="text-error px-1 pt-2 text-sm"
        >
          {{ nqError }}
        </div>
        <div
          v-if="nqProgress"
          class="settings-grid mt-2"
        >
          <DataLine :label="$t('idleLatency')">
            {{ nqProgress.idleLatencyMs > 0 ? `${nqProgress.idleLatencyMs} ms` : '-' }}
          </DataLine>
          <DataLine :label="$t('download')">
            <ResultValue
              :pending="nqRunning && !nqFinished && nqProgress.phase === 1"
              :value="
                nqProgress.downloadCapacity > 0n ? formatBitrate(nqProgress.downloadCapacity) : '-'
              "
            >
              <AccuracyBadge
                v-if="nqFinished && nqProgress.downloadCapacity > 0n"
                :accuracy="nqProgress.downloadCapacityAccuracy"
              />
            </ResultValue>
          </DataLine>
          <DataLine :label="$t('upload')">
            <ResultValue
              :pending="nqRunning && !nqFinished && nqProgress.phase === 2"
              :value="
                nqProgress.uploadCapacity > 0n ? formatBitrate(nqProgress.uploadCapacity) : '-'
              "
            >
              <AccuracyBadge
                v-if="nqFinished && nqProgress.uploadCapacity > 0n"
                :accuracy="nqProgress.uploadCapacityAccuracy"
              />
            </ResultValue>
          </DataLine>
          <DataLine :label="$t('downloadRPM')">
            <ResultValue
              :pending="nqRunning && !nqFinished && nqProgress.phase === 1"
              :value="nqProgress.downloadRPM > 0 ? String(nqProgress.downloadRPM) : '-'"
            >
              <AccuracyBadge
                v-if="nqFinished && nqProgress.downloadRPM > 0"
                :accuracy="nqProgress.downloadRPMAccuracy"
              />
            </ResultValue>
          </DataLine>
          <DataLine :label="$t('uploadRPM')">
            <ResultValue
              :pending="nqRunning && !nqFinished && nqProgress.phase === 2"
              :value="nqProgress.uploadRPM > 0 ? String(nqProgress.uploadRPM) : '-'"
            >
              <AccuracyBadge
                v-if="nqFinished && nqProgress.uploadRPM > 0"
                :accuracy="nqProgress.uploadRPMAccuracy"
              />
            </ResultValue>
          </DataLine>
          <DataLine :label="$t('elapsed')">
            {{ `${(Number(nqProgress.elapsedMs) / 1000).toFixed(1)} s` }}
          </DataLine>
        </div>
        <div
          v-else-if="nqRunning"
          class="flex items-center gap-2 px-1 pt-2 text-sm opacity-70"
        >
          <span class="loading loading-spinner loading-xs"></span>
          {{ $t('fetchingConfiguration') }}
        </div>
      </section>

      <!-- STUN Test -->
      <section class="flex flex-col gap-1">
        <div class="settings-section-label">
          {{ $t('stunTest') }} - {{ $t('natTypeDetection') }}
        </div>
        <div class="settings-grid">
          <div class="setting-item">
            <div class="setting-item-label">{{ $t('serverField') }}</div>
            <input
              class="input input-sm w-48 max-w-full sm:w-72"
              v-model="stunServer"
              :disabled="stunRunning"
            />
          </div>
          <div class="setting-item">
            <div class="setting-item-label">{{ $t('outbound') }}</div>
            <select
              class="select select-sm min-w-24"
              v-model="stunOutbound"
              :disabled="stunRunning"
            >
              <option value="">{{ $t('default') }}</option>
              <option
                v-for="tag in outboundTags"
                :key="tag"
                :value="tag"
              >
                {{ tag }}
              </option>
            </select>
          </div>
          <div class="setting-item">
            <button
              v-if="!stunRunning"
              class="btn btn-neutral btn-sm ml-auto"
              @click="startStun"
            >
              {{ $t('startTest') }}
            </button>
            <button
              v-else
              class="btn btn-sm ml-auto"
              @click="cancelStun"
            >
              {{ $t('cancelTest') }}
            </button>
          </div>
        </div>

        <div
          v-if="stunError"
          class="text-error px-1 pt-2 text-sm"
        >
          {{ stunError }}
        </div>
        <div
          v-if="stunResult"
          class="settings-grid mt-2"
        >
          <DataLine :label="$t('externalAddress')">{{ stunResult.externalAddr || '-' }}</DataLine>
          <DataLine :label="$t('latency')">
            {{ stunResult.latencyMs > 0 ? `${stunResult.latencyMs} ms` : '-' }}
          </DataLine>
          <DataLine
            v-if="stunResult.isFinal && !stunResult.natTypeSupported"
            :label="$t('natTypeDetection')"
          >
            {{ $t('notSupportedByServer') }}
          </DataLine>
          <template v-else>
            <DataLine :label="$t('natMapping')">
              <span
                v-if="stunResult.natMapping > 0"
                class="badge badge-sm"
                :class="toneBadge(natMappingTone(stunResult.natMapping))"
              >
                {{ natMappingDescription(stunResult.natMapping) }}
              </span>
              <span
                v-else-if="stunRunning"
                class="loading loading-spinner loading-xs"
              ></span>
              <span v-else>-</span>
            </DataLine>
            <DataLine :label="$t('natFiltering')">
              <span
                v-if="stunResult.natFiltering > 0"
                class="badge badge-sm"
                :class="toneBadge(natFilteringTone(stunResult.natFiltering))"
              >
                {{ natFilteringDescription(stunResult.natFiltering) }}
              </span>
              <span
                v-else-if="stunRunning"
                class="loading loading-spinner loading-xs"
              ></span>
              <span v-else>-</span>
            </DataLine>
          </template>
        </div>
        <div
          v-else-if="stunRunning"
          class="flex items-center gap-2 px-1 pt-2 text-sm opacity-70"
        >
          <span class="loading loading-spinner loading-xs"></span>
          {{ $t('binding') }}
        </div>
      </section>
    </div>
  </div>
</template>

<script setup lang="ts">
import { getSingboxClient } from '@/assembly/tools'
import type { NetworkQualityTestProgress, STUNTestProgress } from '@/gen/daemon/started_service_pb'
import { proxyMap } from '@/assembly/proxies'
import { computed, defineComponent, h, onBeforeUnmount, ref } from 'vue'
import { useI18n } from 'vue-i18n'

const NETWORK_QUALITY_DEFAULT_URL = 'https://mensura.cdn-apple.com/api/v1/gm/config'
const STUN_DEFAULT_SERVER = 'stun.voipgate.com:3478'

const { t } = useI18n()

// A label/value result row in the settings-grid style.
const DataLine = defineComponent({
  props: { label: String },
  setup:
    (props, { slots }) =>
    () =>
      h('div', { class: 'setting-item' }, [
        h('div', { class: 'setting-item-label' }, props.label),
        h('span', { class: 'text-right font-medium break-all' }, slots.default?.()),
      ]),
})

// Value cell with an optional pending spinner and trailing badge slot.
const ResultValue = defineComponent({
  props: { pending: Boolean, value: String },
  setup:
    (props, { slots }) =>
    () =>
      h('span', { class: 'inline-flex items-center gap-2' }, [
        props.pending ? h('span', { class: 'loading loading-spinner loading-xs' }) : null,
        props.value,
        slots.default?.(),
      ]),
})

const AccuracyBadge = defineComponent({
  props: { accuracy: { type: Number, required: true } },
  setup: (props) => () => {
    const map: Record<number, [string, string]> = {
      2: ['accuracyHigh', 'badge-success'],
      1: ['accuracyMedium', 'badge-warning'],
    }
    const [key, cls] = map[props.accuracy] ?? ['accuracyLow', 'badge-error']
    return h('span', { class: `badge badge-xs ${cls}` }, t(key))
  },
})

const client = () => getSingboxClient()
const outboundTags = computed(() => Object.keys(proxyMap.value))

const formatBitrate = (bps: number | bigint): string => {
  const value = Number(bps)
  if (value >= 1_000_000_000) return `${(value / 1_000_000_000).toFixed(1)} Gbps`
  if (value >= 1_000_000) return `${(value / 1_000_000).toFixed(1)} Mbps`
  if (value >= 1_000) return `${(value / 1_000).toFixed(1)} kbps`
  return `${Math.round(value)} bps`
}

type Tone = 'neutral' | 'good' | 'medium' | 'bad'

const toneBadge = (tone: Tone): string =>
  ({ good: 'badge-success', medium: 'badge-warning', bad: 'badge-error', neutral: 'badge-ghost' })[
    tone
  ]

// NAT mapping and filtering use different enum offsets in sing-box.
const natMappingDescription = (v: number): string =>
  ({ 2: 'Endpoint Independent', 3: 'Address Dependent', 4: 'Address and Port Dependent' })[v] ??
  'Unknown'
const natFilteringDescription = (v: number): string =>
  ({ 1: 'Endpoint Independent', 2: 'Address Dependent', 3: 'Address and Port Dependent' })[v] ??
  'Unknown'
const natMappingTone = (v: number): Tone =>
  (({ 2: 'good', 3: 'medium', 4: 'bad' }) as Record<number, Tone>)[v] ?? 'neutral'
const natFilteringTone = (v: number): Tone =>
  (({ 1: 'good', 2: 'medium', 3: 'bad' }) as Record<number, Tone>)[v] ?? 'neutral'

// --- Network Quality ---
const nqConfigURL = ref(NETWORK_QUALITY_DEFAULT_URL)
const nqOutbound = ref('')
const nqSerial = ref(false)
const nqHttp3 = ref(false)
const nqMaxRuntime = ref(20)
const nqRunning = ref(false)
const nqError = ref('')
const nqProgress = ref<NetworkQualityTestProgress>()
const nqFinished = computed(() => nqProgress.value?.isFinal ?? false)
let nqController: AbortController | null = null

const startNetworkQuality = async () => {
  const c = client()
  if (!c) return
  nqRunning.value = true
  nqError.value = ''
  nqProgress.value = undefined
  nqController = new AbortController()
  try {
    for await (const update of c.client.startNetworkQualityTest(
      {
        configURL: nqConfigURL.value,
        outboundTag: nqOutbound.value,
        http3: nqHttp3.value,
        maxRuntimeSeconds: nqMaxRuntime.value,
        serial: nqSerial.value,
      },
      { signal: nqController.signal },
    )) {
      nqProgress.value = update
      if (update.error) nqError.value = update.error
      if (update.isFinal) break
    }
  } catch (e) {
    if (!nqController.signal.aborted) nqError.value = String(e)
  } finally {
    nqRunning.value = false
  }
}

const cancelNetworkQuality = () => {
  nqController?.abort()
  nqRunning.value = false
}

// --- STUN ---
const stunServer = ref(STUN_DEFAULT_SERVER)
const stunOutbound = ref('')
const stunRunning = ref(false)
const stunError = ref('')
const stunResult = ref<STUNTestProgress>()
let stunController: AbortController | null = null

const startStun = async () => {
  const c = client()
  if (!c) return
  stunRunning.value = true
  stunError.value = ''
  stunResult.value = undefined
  stunController = new AbortController()
  try {
    for await (const update of c.client.startSTUNTest(
      { server: stunServer.value, outboundTag: stunOutbound.value },
      { signal: stunController.signal },
    )) {
      stunResult.value = update
      if (update.error) stunError.value = update.error
      if (update.isFinal) break
    }
  } catch (e) {
    if (!stunController.signal.aborted) stunError.value = String(e)
  } finally {
    stunRunning.value = false
  }
}

const cancelStun = () => {
  stunController?.abort()
  stunRunning.value = false
}

onBeforeUnmount(() => {
  nqController?.abort()
  stunController?.abort()
})
</script>
