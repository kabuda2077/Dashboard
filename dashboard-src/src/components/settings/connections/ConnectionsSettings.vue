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
      <div class="setting-item">
        <label
          class="setting-item-label"
          for="resolve-client-hostname"
          >{{ $t('resolveClientHostname') }}</label
        >
        <input
          id="resolve-client-hostname"
          v-model="resolveClientHostname"
          type="checkbox"
          class="toggle toggle-sm"
        />
      </div>
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

    <template v-if="canShowTunStack">
      <div class="settings-section-label">{{ $t('tunSettings') }}</div>
      <div class="settings-grid">
        <div class="setting-item">
          <div
            class="setting-item-label"
            @mouseenter="(event) => showTip(event, $t('tunStackHelp'))"
          >
            {{ $t('tunStack') }}
          </div>
          <SelectInput
            :model-value="tunStack"
            :options="tunStackOptions"
            :disabled="!canWriteTun"
            :aria-busy="isTunBusy"
            :aria-label="$t('tunStack')"
            :aria-description="$t('tunStackHelp')"
            class="select-sm min-w-24 shrink-0"
            @mouseenter="(event: MouseEvent) => showTip(event, $t('tunStackHelp'))"
            @focus="(event: FocusEvent) => showTip(event, $t('tunStackHelp'))"
            @update:model-value="handlerTunStackChange"
          />
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import SelectInput from '@/components/common/SelectInput.vue'
import { useBackendRuntimeConfig } from '@/composables/useBackendRuntimeConfig'
import { runManualRequest } from '@/helper/request-error'
import { useTooltip } from '@/helper/tooltip'
import { i18n } from '@/i18n'
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
  resolveClientHostname,
  tableSize,
  tableWidthMode,
} from '@/store/settings'
import { configs } from '@/assembly/config'
import { computed } from 'vue'

const { showTip } = useTooltip()
const { tunStack, tunStackOptions, canShowTunStack, canWriteTun, isTunBusy, updateTunStack } =
  useBackendRuntimeConfig()
const handlerTunStackChange = (stack: string) =>
  runManualRequest(async () => {
    if ((await updateTunStack(stack)) === false)
      throw new Error(i18n.global.t('tunStackUpdateFailed'))
  })

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
