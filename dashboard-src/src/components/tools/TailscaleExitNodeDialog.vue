<template>
  <DialogWrapper
    v-model="isOpen"
    :title="$t('exitNode')"
  >
    <div class="flex flex-col gap-3">
      <label class="flex flex-col gap-1 text-sm">
        <span>{{ $t('search') }}</span>
        <input
          ref="searchEl"
          class="input input-sm input-bordered"
          v-model="search"
        />
      </label>
      <div class="flex flex-col gap-1">
        <button
          class="hover:bg-base-200 flex items-center gap-2 rounded p-2 text-left text-sm"
          @click="select('')"
        >
          <span class="flex-1">{{ $t('disabledLabel') }}</span>
          <CheckIcon
            v-if="current === ''"
            class="h-4 w-4"
          />
        </button>
        <button
          v-for="peer in filtered"
          :key="peer.stableID"
          class="hover:bg-base-200 flex items-center gap-2 rounded p-2 text-left text-sm"
          @click="select(peer.stableID)"
        >
          <span
            class="inline-block h-2 w-2 flex-shrink-0 rounded-full"
            :class="peer.online ? 'bg-success' : 'bg-base-300'"
          ></span>
          <span class="font-medium">{{ peerDisplayName(peer) }}</span>
          <span class="opacity-60">{{ peer.tailscaleIPs[0] }}</span>
          <span class="flex-1"></span>
          <CheckIcon
            v-if="current === peer.stableID"
            class="h-4 w-4"
          />
        </button>
      </div>
    </div>
  </DialogWrapper>
</template>

<script setup lang="ts">
import DialogWrapper from '@/components/common/DialogWrapper.vue'
import { getSingboxClient } from '@/api/singbox/client'
import { peerDisplayName } from '@/composables/tailscaleSSH'
import type { TailscaleEndpointStatus, TailscalePeer } from '@/gen/daemon/started_service_pb'
import { CheckIcon } from '@heroicons/vue/24/outline'
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'

const props = defineProps<{ endpoint: TailscaleEndpointStatus; candidates: TailscalePeer[] }>()
const isOpen = defineModel<boolean>()

const searchEl = useTemplateRef<HTMLInputElement>('searchEl')
const search = ref('')

const current = computed(() => props.endpoint.exitNode?.stableID ?? '')

const filtered = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (query === '') return props.candidates
  return props.candidates.filter(
    (peer) =>
      peerDisplayName(peer).toLowerCase().includes(query) ||
      peer.hostName.toLowerCase().includes(query) ||
      peer.dnsName.toLowerCase().includes(query) ||
      peer.tailscaleIPs.some((address) => address.includes(query)),
  )
})

watch(isOpen, async (open) => {
  if (!open) return
  search.value = ''
  await nextTick()
  searchEl.value?.focus()
})

const select = (stableID: string) => {
  getSingboxClient()?.client.setTailscaleExitNode({
    endpointTag: props.endpoint.endpointTag,
    stableID,
  })
  isOpen.value = false
}
</script>
