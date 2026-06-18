<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-4 p-2 sm:p-4">
    <div
      v-if="endpoints.length === 0"
      class="text-base-content/60 p-8 text-center text-sm"
    >
      {{ $t('noEndpoints') }}
    </div>

    <div
      v-for="endpoint in endpoints"
      :key="endpoint.endpointTag"
      class="card bg-base-100"
    >
      <div class="card-body gap-3 p-4">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 class="card-title text-base">
            {{ endpoint.endpointTag || 'Tailscale' }}
            <span
              class="badge badge-sm"
              :class="stateBadge(endpoint.backendState)"
              >{{ endpoint.backendState || $t('unknown') }}</span
            >
          </h2>
          <button
            v-if="!endpoint.keyAuth"
            class="btn btn-xs btn-error btn-outline"
            @click="logout(endpoint.endpointTag)"
          >
            {{ $t('logout') }}
          </button>
        </div>

        <!-- Status section -->
        <div class="border-base-300 divide-base-300 divide-y rounded border text-sm">
          <button
            v-if="endpoint.self"
            class="hover:bg-base-200 flex w-full cursor-pointer items-center justify-between gap-3 px-3 py-2 text-left"
            @click="openPeerDetail(endpoint, endpoint.self, true)"
          >
            <span class="opacity-60">{{ $t('thisDevice') }}</span>
            <span class="font-medium break-all">{{ peerDisplayName(endpoint.self) }}</span>
          </button>
          <button
            v-if="exitCandidates(endpoint).length > 0"
            class="hover:bg-base-200 flex w-full cursor-pointer items-center justify-between gap-3 px-3 py-2 text-left"
            @click="openExitPicker(endpoint)"
          >
            <span class="opacity-60">{{ $t('exitNode') }}</span>
            <span class="font-medium break-all">
              {{ endpoint.exitNode ? peerDisplayName(endpoint.exitNode) : $t('disabledLabel') }}
            </span>
          </button>
          <div
            v-if="endpoint.networkName"
            class="flex items-center justify-between gap-3 px-3 py-2"
          >
            <span class="opacity-60">{{ $t('networkLabel') }}</span>
            <span class="font-medium break-all">{{ endpoint.networkName }}</span>
          </div>
          <div
            v-if="endpoint.authURL"
            class="flex flex-wrap items-center justify-between gap-2 px-3 py-2"
          >
            <a
              :href="endpoint.authURL"
              target="_blank"
              class="link link-primary break-all"
              >{{ endpoint.authURL }}</a
            >
            <button
              class="btn btn-xs"
              @click="openAuthQR(endpoint)"
            >
              {{ $t('showAuthQR') }}
            </button>
          </div>
        </div>

        <!-- Peers grouped by user -->
        <template
          v-for="group in groupsOf(endpoint)"
          :key="group.userID.toString()"
        >
          <div class="text-xs font-medium opacity-60">
            {{ group.displayName || group.loginName || $t('peers') }}
          </div>
          <div class="flex flex-col gap-1">
            <div
              v-for="peer in group.peers"
              :key="peer.stableID"
              class="bg-base-200 flex flex-col gap-2 rounded p-2 text-sm sm:flex-row sm:flex-wrap sm:items-center"
            >
              <button
                class="flex min-w-0 items-center gap-2 text-left"
                @click="openPeerDetail(endpoint, peer, false)"
              >
                <span
                  class="inline-block h-2 w-2 flex-shrink-0 rounded-full"
                  :class="peer.online ? 'bg-success' : 'bg-base-300'"
                ></span>
                <span class="font-medium">{{ peerDisplayName(peer) }}</span>
                <span class="truncate opacity-60">{{ peer.tailscaleIPs[0] }}</span>
              </button>
              <div
                v-if="hasPeerMeta(peer)"
                class="flex flex-1 flex-wrap items-center gap-2"
              >
                <div class="flex flex-1 items-center gap-1">
                  <span
                    v-if="peer.exitNode || peer.exitNodeOption"
                    class="badge badge-xs"
                    :class="peer.exitNode ? 'badge-primary' : 'badge-info'"
                    >{{ $t('exitNode') }}</span
                  >
                  <span
                    v-if="peer.shareeNode"
                    class="badge badge-xs badge-error"
                    >{{ $t('sharedIn') }}</span
                  >
                  <span
                    v-if="peer.expired"
                    class="badge badge-xs badge-error"
                    >{{ $t('expired') }}</span
                  >
                  <span
                    v-if="peer.sshHostKeys.length > 0"
                    class="badge badge-xs badge-info"
                    >SSH</span
                  >
                </div>
                <button
                  v-if="peerSSHAvailable(peer)"
                  class="btn btn-xs btn-primary btn-outline leading-none"
                  @click="connectSSH(endpoint, peer)"
                >
                  {{ $t('connectViaSSH') }}
                </button>
              </div>
            </div>
          </div>
        </template>
      </div>
    </div>

    <!-- Dialogs -->
    <TailscalePeerDialog
      v-if="peerDetail"
      v-model="peerDetailOpen"
      :endpoint="peerDetail.endpoint"
      :peer="peerDetail.peer"
      :is-self="peerDetail.isSelf"
      @connect-ssh="onPeerDetailConnectSSH"
      @edit-ssh="onPeerDetailEditSSH"
    />
    <TailscaleExitNodeDialog
      v-if="exitPicker"
      v-model="exitPickerOpen"
      :endpoint="exitPicker.endpoint"
      :candidates="exitPicker.candidates"
    />
    <TailscaleSSHDialog
      v-if="sshPrompt"
      v-model="sshPromptOpen"
      :peer="sshPrompt.peer"
      @connect="onSSHPromptConnect"
    />
    <DialogWrapper
      v-model="authQROpen"
      :title="$t('authURL')"
    >
      <div
        v-if="authQR"
        class="flex flex-col items-center gap-3"
      >
        <QRCodeView :value="authQR.authURL" />
        <a
          :href="authQR.authURL"
          target="_blank"
          class="link link-primary text-xs break-all"
          >{{ authQR.authURL }}</a
        >
      </div>
    </DialogWrapper>
  </div>
</template>

<script setup lang="ts">
import { getSingboxClient } from '@/api/singbox/client'
import { runStream, type StreamHandle } from '@/api/singbox/streams'
import DialogWrapper from '@/components/common/DialogWrapper.vue'
import QRCodeView from '@/components/tools/QRCodeView.vue'
import TailscaleExitNodeDialog from '@/components/tools/TailscaleExitNodeDialog.vue'
import TailscalePeerDialog from '@/components/tools/TailscalePeerDialog.vue'
import TailscaleSSHDialog from '@/components/tools/TailscaleSSHDialog.vue'
import {
  buildSSHSession,
  peerDisplayName,
  peerSSHAvailable,
  saveSSHPrefs,
  sshPrefs,
  type SSHSessionOptions,
} from '@/composables/tailscaleSSH'
import type {
  TailscaleEndpointStatus,
  TailscalePeer,
  TailscaleUserGroup,
} from '@/gen/daemon/started_service_pb'
import { onBeforeUnmount, onMounted, ref } from 'vue'

const emit = defineEmits<{ ssh: [session: SSHSessionOptions] }>()

const endpoints = ref<TailscaleEndpointStatus[]>([])
let statusHandle: StreamHandle | null = null

const groupsOf = (endpoint: TailscaleEndpointStatus): TailscaleUserGroup[] =>
  endpoint.userGroups.filter((g) => g.peers.length > 0)

const hasPeerMeta = (peer: TailscalePeer): boolean =>
  peer.exitNode ||
  peer.exitNodeOption ||
  peer.shareeNode ||
  peer.expired ||
  peer.sshHostKeys.length > 0

const exitCandidates = (endpoint: TailscaleEndpointStatus): TailscalePeer[] =>
  endpoint.backendState === 'Running'
    ? endpoint.userGroups.flatMap((g) => g.peers).filter((p) => p.exitNodeOption)
    : []

const stateBadge = (state: string): string => {
  switch (state) {
    case 'Running':
      return 'badge-success'
    case 'NeedsLogin':
    case 'NeedsMachineAuth':
      return 'badge-error'
    case 'Starting':
      return 'badge-warning'
    default:
      return 'badge-ghost'
  }
}

const logout = (endpointTag: string) => {
  getSingboxClient()?.client.tailscaleLogout({ endpointTag })
}

// --- Dialog state ---
const peerDetail = ref<{
  endpoint: TailscaleEndpointStatus
  peer: TailscalePeer
  isSelf: boolean
}>()
const peerDetailOpen = ref(false)
const openPeerDetail = (
  endpoint: TailscaleEndpointStatus,
  peer: TailscalePeer,
  isSelf: boolean,
) => {
  peerDetail.value = { endpoint, peer, isSelf }
  peerDetailOpen.value = true
}

const exitPicker = ref<{ endpoint: TailscaleEndpointStatus; candidates: TailscalePeer[] }>()
const exitPickerOpen = ref(false)
const openExitPicker = (endpoint: TailscaleEndpointStatus) => {
  exitPicker.value = { endpoint, candidates: exitCandidates(endpoint) }
  exitPickerOpen.value = true
}

const authQR = ref<TailscaleEndpointStatus>()
const authQROpen = ref(false)
const openAuthQR = (endpoint: TailscaleEndpointStatus) => {
  authQR.value = endpoint
  authQROpen.value = true
}

const sshPrompt = ref<{ endpoint: TailscaleEndpointStatus; peer: TailscalePeer }>()
const sshPromptOpen = ref(false)
const openSSHPrompt = (endpoint: TailscaleEndpointStatus, peer: TailscalePeer) => {
  sshPrompt.value = { endpoint, peer }
  sshPromptOpen.value = true
}

// --- SSH launch ---
const launchSSH = (
  endpoint: TailscaleEndpointStatus,
  peer: TailscalePeer,
  username: string,
  terminalType: string,
) => {
  emit('ssh', buildSSHSession(endpoint.endpointTag, peer, username, terminalType))
  peerDetailOpen.value = false
}

const connectSSH = (endpoint: TailscaleEndpointStatus, peer: TailscalePeer) => {
  const prefs = sshPrefs.value[peer.stableID]
  if (prefs?.remember) {
    launchSSH(endpoint, peer, prefs.username, prefs.terminalType)
  } else {
    openSSHPrompt(endpoint, peer)
  }
}

// Triggered from inside the peer detail dialog: close it first so the SSH
// prompt / terminal isn't hidden behind it (both dialogs share one layer).
const onPeerDetailConnectSSH = () => {
  const ctx = peerDetail.value
  if (!ctx) return
  peerDetailOpen.value = false
  connectSSH(ctx.endpoint, ctx.peer)
}

const onPeerDetailEditSSH = () => {
  const ctx = peerDetail.value
  if (!ctx) return
  peerDetailOpen.value = false
  openSSHPrompt(ctx.endpoint, ctx.peer)
}

const onSSHPromptConnect = (username: string, terminalType: string, remember: boolean) => {
  const ctx = sshPrompt.value
  if (!ctx) return
  if (remember) {
    saveSSHPrefs(ctx.peer.stableID, { username, terminalType, remember })
  }
  launchSSH(ctx.endpoint, ctx.peer, username, terminalType)
}

onMounted(() => {
  const c = getSingboxClient()
  if (!c) return
  statusHandle = runStream(
    (signal) => c.client.subscribeTailscaleStatus({}, { signal }),
    (msg) => (endpoints.value = msg.endpoints),
  )
})

onBeforeUnmount(() => statusHandle?.close())
</script>
