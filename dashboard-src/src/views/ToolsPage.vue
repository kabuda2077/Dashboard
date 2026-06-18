<template>
  <div class="relative flex h-full min-h-0 flex-col">
    <CtrlsBar>
      <div class="flex items-center gap-2 p-2">
        <div
          role="tablist"
          class="tabs-box tabs tabs-xs w-full sm:w-fit"
        >
          <a
            v-for="tab in TOOLS_TABS"
            :key="tab.key"
            role="tab"
            :class="twMerge('tab flex-1 gap-1 sm:flex-none', activeTab === tab.key && 'tab-active')"
            @click="activeTab = tab.key"
          >
            <component
              :is="tab.icon"
              class="h-4 w-4 flex-shrink-0"
            />
            {{ $t(tab.key) }}
          </a>
        </div>
      </div>
    </CtrlsBar>

    <div
      class="min-h-0 flex-1 overflow-y-auto"
      :style="padding"
    >
      <KeepAlive>
        <NetworkToolsPanel v-if="activeTab === TOOLS_TAB_TYPE.network" />
        <TailscalePanel
          v-else
          @ssh="openSSH"
        />
      </KeepAlive>
    </div>

    <!-- SSH terminal overlay: only reachable by connecting to a Tailscale peer.
         While open, TerminalPanel tracks the visual viewport (useViewportHeight),
         so this content area already shrinks above the soft keyboard. -->
    <div
      v-if="sshSession"
      class="absolute inset-0 z-200"
    >
      <TerminalPanel
        :launch="sshSession"
        @close="sshSession = undefined"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import CtrlsBar from '@/components/common/CtrlsBar.vue'
import NetworkToolsPanel from '@/components/tools/NetworkToolsPanel.vue'
import TailscalePanel from '@/components/tools/TailscalePanel.vue'
import TerminalPanel from '@/components/tools/TerminalPanel.vue'
import { usePaddingForViews } from '@/composables/paddingViews'
import type { SSHSessionOptions } from '@/composables/tailscaleSSH'
import { ShareIcon, WrenchScrewdriverIcon } from '@heroicons/vue/24/outline'
import { twMerge } from 'tailwind-merge'
import { ref } from 'vue'

enum TOOLS_TAB_TYPE {
  network = 'tools',
  tailscale = 'tailscale',
}

const TOOLS_TABS = [
  { key: TOOLS_TAB_TYPE.network, icon: WrenchScrewdriverIcon },
  { key: TOOLS_TAB_TYPE.tailscale, icon: ShareIcon },
] as const

const activeTab = ref<TOOLS_TAB_TYPE>(TOOLS_TAB_TYPE.network)

// On mobile the CtrlsBar floats fixed over the top, so the scroll area needs
// padding to keep its content clear of the bar.
const { padding } = usePaddingForViews({
  offsetTop: 0,
  offsetBottom: 8,
})

// The terminal is reachable only by launching SSH from a Tailscale peer; it
// opens as an overlay over the tools page and closing it returns here. The
// `seq` re-mounts the terminal even when connecting to the same peer again.
const sshSession = ref<SSHSessionOptions & { seq: number }>()
const openSSH = (session: SSHSessionOptions) => {
  sshSession.value = { ...session, seq: (sshSession.value?.seq ?? 0) + 1 }
}
</script>
