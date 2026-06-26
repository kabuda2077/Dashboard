<template>
  <!-- connections -->
  <div class="flex flex-col gap-3 text-sm">
    <div class="settings-grid">
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('connectionStyle') }}
        </div>
        <select
          class="select select-sm min-w-24"
          v-model="connectionDisplayStyle"
        >
          <option :value="CONNECTION_DISPLAY_STYLE.AUTO">
            {{ $t('auto') }}
          </option>
          <option :value="CONNECTION_DISPLAY_STYLE.CARD">
            {{ $t('card') }}
          </option>
          <option :value="CONNECTION_DISPLAY_STYLE.TABLE">
            {{ $t('table') }}
          </option>
        </select>
      </div>
      <div class="setting-item">
        <div class="setting-item-label">
          {{ $t('proxyChainDirection') }}
        </div>
        <select
          class="select select-sm w-24"
          v-model="proxyChainDirection"
        >
          <option
            v-for="opt in Object.values(PROXY_CHAIN_DIRECTION)"
            :key="opt"
            :value="opt"
          >
            {{ $t(opt) }}
          </option>
        </select>
      </div>
      <template v-if="!isConnectionCard">
        <div class="setting-item">
          <div class="setting-item-label">
            {{ $t('tableWidthMode') }}
          </div>
          <select
            class="select select-sm min-w-24"
            v-model="tableWidthMode"
          >
            <option
              v-for="opt in Object.values(TABLE_WIDTH_MODE)"
              :key="opt"
              :value="opt"
            >
              {{ $t(opt) }}
            </option>
          </select>
        </div>
        <div class="setting-item">
          <div class="setting-item-label">
            {{ $t('tableSize') }}
          </div>
          <select
            class="select select-sm min-w-24"
            v-model="tableSize"
          >
            <option
              v-for="opt in Object.values(TABLE_SIZE)"
              :key="opt"
              :value="opt"
            >
              {{ $t(opt) }}
            </option>
          </select>
        </div>
      </template>
      <SourceIPLabels />
    </div>

    <template v-if="configs && canShowPortsGrid">
      <div class="settings-section-label">端口设置</div>
      <div class="settings-grid">
        <div class="setting-panel-row">
          <BackendPortsGrid />
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import BackendPortsGrid from '@/components/settings/backend/BackendPortsGrid.vue'
import SourceIPLabels from '@/components/settings/connections/SourceIPLabels.vue'
import {
  CONNECTION_DISPLAY_STYLE,
  PROXY_CHAIN_DIRECTION,
  TABLE_SIZE,
  TABLE_WIDTH_MODE,
} from '@/constant'
import {
  connectionDisplayStyle,
  isConnectionCard,
  proxyChainDirection,
  tableSize,
  tableWidthMode,
} from '@/store/settings'
import { configs } from '@/assembly/config'
import { computed } from 'vue'

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
</script>
