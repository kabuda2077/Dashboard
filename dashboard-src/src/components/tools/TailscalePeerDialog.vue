<template>
  <DialogWrapper
    v-model="isOpen"
    :title="peerDisplayName(peer)"
  >
    <template #title-right>
      <span
        class="badge badge-sm ml-2"
        :class="peer.online ? 'badge-success' : 'badge-ghost'"
      >
        {{ peer.online ? $t('connected') : $t('notConnected') }}
      </span>
    </template>

    <div class="flex flex-col gap-4 text-sm">
      <!-- Addresses -->
      <section class="flex flex-col gap-1">
        <div class="text-xs font-medium opacity-60">{{ $t('addresses') }}</div>
        <div class="border-base-300 divide-base-300 divide-y rounded border">
          <CopyLine
            v-if="magicDNS"
            label="MagicDNS"
            :value="magicDNS"
          />
          <CopyLine
            :label="$t('hostnameLabel')"
            :value="peer.hostName"
          />
          <CopyLine
            v-if="ipv4"
            label="IPv4"
            :value="ipv4"
          />
          <CopyLine
            v-if="ipv6"
            label="IPv6"
            :value="ipv6"
          />
        </div>
      </section>

      <!-- Ping -->
      <section
        v-if="!isSelf && peer.online"
        class="flex flex-col gap-1"
      >
        <div class="flex items-center justify-between">
          <span class="text-xs font-medium opacity-60">{{ $t('ping') }}</span>
          <button
            class="btn btn-ghost btn-xs"
            @click="pingRunning ? stopPing() : startPing()"
          >
            <component
              :is="pingRunning ? StopIcon : PlayIcon"
              class="h-4 w-4"
            />
          </button>
        </div>
        <div
          v-if="pingError"
          class="text-error py-1"
        >
          {{ pingError }}
        </div>
        <template v-if="pingLatest">
          <div class="border-base-300 divide-base-300 divide-y rounded border">
            <DataRow :label="pingLatest.isDirect ? $t('directConnection') : $t('derpRelayed')">
              {{ pingLatest.latencyMs.toFixed(1) }} ms
            </DataRow>
            <DataRow
              v-if="!pingLatest.isDirect && pingLatest.derpRegionCode"
              :label="$t('derpRegion')"
            >
              {{ pingLatest.derpRegionCode }}
            </DataRow>
            <DataRow
              v-if="pingLatest.isDirect && pingLatest.endpoint"
              :label="$t('endpointLabel')"
            >
              {{ pingLatest.endpoint }}
            </DataRow>
          </div>
          <PingSparkline
            :data="pingHistory"
            :color="pingLatest.isDirect ? 'var(--color-success)' : 'var(--color-info)'"
            class="text-primary mt-1"
          />
        </template>
        <div
          v-else-if="pingRunning"
          class="py-1 opacity-60"
        >
          {{ $t('connecting') }}
        </div>
        <div
          v-else
          class="py-1 opacity-60"
        >
          {{ $t('noData') }}
        </div>
      </section>

      <!-- Details -->
      <section class="flex flex-col gap-1">
        <div class="text-xs font-medium opacity-60">{{ $t('detailsLabel') }}</div>
        <div class="border-base-300 divide-base-300 divide-y rounded border">
          <DataRow
            v-if="peer.os"
            :label="$t('osLabel')"
          >
            {{ peer.os }}
          </DataRow>
          <DataRow :label="$t('keyExpiry')">{{ keyExpiryText }}</DataRow>
          <DataRow
            v-if="!peer.online && peer.lastSeen > 0n"
            :label="$t('lastSeen')"
          >
            {{ fromNow(peer.lastSeen) }}
          </DataRow>
          <DataRow
            v-if="peer.exitNodeOption"
            :label="$t('exitNode')"
          >
            {{ peer.exitNode ? $t('activeLabel') : $t('availableLabel') }}
          </DataRow>
          <DataRow
            v-if="peer.shareeNode"
            :label="$t('sharedIn')"
          >
            {{ $t('yes') }}
          </DataRow>
        </div>
      </section>

      <!-- SSH actions -->
      <div
        v-if="sshAvailable"
        class="flex justify-end gap-2"
      >
        <button
          v-if="sshRemembered"
          class="btn btn-sm"
          @click="emit('editSsh')"
        >
          {{ $t('editSSHConfiguration') }}
        </button>
        <button
          class="btn btn-primary btn-sm"
          @click="emit('connectSsh')"
        >
          {{ $t('connectViaSSH') }}
        </button>
      </div>
    </div>
  </DialogWrapper>
</template>

<script setup lang="ts">
import { getSingboxClient } from '@/api/singbox/client'
import DialogWrapper from '@/components/common/DialogWrapper.vue'
import PingSparkline from '@/components/tools/PingSparkline.vue'
import { peerDisplayName, sshPrefs } from '@/composables/tailscaleSSH'
import type {
  TailscaleEndpointStatus,
  TailscalePeer,
  TailscalePingResponse,
} from '@/gen/daemon/started_service_pb'
import { showNotification } from '@/helper/notification'
import { DocumentDuplicateIcon, PlayIcon, StopIcon } from '@heroicons/vue/24/outline'
import { useClipboard } from '@vueuse/core'
import dayjs from 'dayjs'
import { computed, defineComponent, h, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  endpoint: TailscaleEndpointStatus
  peer: TailscalePeer
  isSelf: boolean
}>()
const isOpen = defineModel<boolean>()
const emit = defineEmits<{ connectSsh: []; editSsh: [] }>()

const { t } = useI18n()
const { copy } = useClipboard()

const DataRow = defineComponent({
  props: { label: String },
  setup:
    (rowProps, { slots }) =>
    () =>
      h('div', { class: 'flex items-center justify-between gap-3 px-3 py-2' }, [
        h('span', { class: 'opacity-60' }, rowProps.label),
        h('span', { class: 'text-right font-medium break-all' }, slots.default?.()),
      ]),
})

const copyValue = (value?: string) => {
  if (!value) return
  copy(value)
  showNotification({ content: 'copySuccess', type: 'alert-success', timeout: 2000 })
}

const CopyLine = defineComponent({
  props: { label: String, value: String },
  setup: (lineProps) => () =>
    h('div', { class: 'flex items-center justify-between gap-3 px-3 py-2' }, [
      h('span', { class: 'opacity-60' }, lineProps.label),
      h('div', { class: 'flex items-center gap-2' }, [
        h('span', { class: 'text-right font-medium break-all' }, lineProps.value),
        h(
          'button',
          {
            class: 'btn btn-ghost btn-xs btn-circle shrink-0',
            onClick: () => copyValue(lineProps.value),
            title: t('copy'),
            'aria-label': t('copy'),
          },
          h(DocumentDuplicateIcon, { class: 'h-4 w-4' }),
        ),
      ]),
    ]),
})

const magicDNS = computed(() => props.peer.dnsName.replace(/\.$/, ''))
const ipv4 = computed(() => props.peer.tailscaleIPs.find((a) => !a.includes(':')))
const ipv6 = computed(() => props.peer.tailscaleIPs.find((a) => a.includes(':')))

const fromNow = (epochSeconds: bigint) => dayjs(Number(epochSeconds) * 1000).fromNow()
const keyExpiryText = computed(() => {
  if (props.peer.expired) return t('expired')
  if (props.peer.keyExpiry > 0n) return fromNow(props.peer.keyExpiry)
  return t('disabledLabel')
})

const sshAvailable = computed(
  () => !props.isSelf && props.peer.online && props.peer.sshHostKeys.length > 0,
)
const sshRemembered = computed(() => sshPrefs.value[props.peer.stableID]?.remember ?? false)

// --- Ping ---
const pingRunning = ref(false)
const pingError = ref('')
const pingLatest = ref<TailscalePingResponse>()
const pingHistory = ref<number[]>([])
let pingController: AbortController | null = null

const startPing = async () => {
  const c = getSingboxClient()
  if (!c) return
  pingRunning.value = true
  pingError.value = ''
  pingLatest.value = undefined
  pingHistory.value = []
  pingController = new AbortController()
  try {
    for await (const res of c.client.startTailscalePing(
      { endpointTag: props.endpoint.endpointTag, peerIP: props.peer.tailscaleIPs[0] ?? '' },
      { signal: pingController.signal },
    )) {
      if (res.error) {
        pingError.value = res.error
        continue
      }
      pingLatest.value = res
      const next = pingHistory.value.concat(res.latencyMs)
      pingHistory.value = next.length > 30 ? next.slice(next.length - 30) : next
    }
  } catch (e) {
    if (!pingController.signal.aborted) pingError.value = String(e)
  } finally {
    pingRunning.value = false
  }
}

const stopPing = () => {
  pingController?.abort()
  pingRunning.value = false
}

// Stop pinging whenever the dialog closes.
watch(isOpen, (open) => {
  if (!open) stopPing()
})

onBeforeUnmount(stopPing)
</script>
