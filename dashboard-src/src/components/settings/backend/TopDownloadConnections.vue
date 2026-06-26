<template>
  <div class="grid h-full min-h-[232px] w-full grid-rows-4 gap-2">
    <template v-if="displayConnections.length">
      <div
        v-for="(connection, index) in displayConnections"
        :key="`${index}-${connection.id}`"
        class="rounded-box bg-base-200/70 flex min-h-0 items-center gap-3 px-3 py-1 text-sm"
      >
        <span
          class="bg-base-100/70 text-base-content/60 flex h-6 min-w-6 shrink-0 items-center justify-center rounded-full px-2 text-xs font-medium"
        >
          {{ index + 1 }}
        </span>
        <div class="min-w-0 flex-1">
          <div class="flex min-w-0 items-center gap-2">
            <span class="truncate font-medium">
              {{ getDisplayValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.Host) }}
            </span>
            <span class="text-base-content/40 shrink-0 text-xs">
              {{ getDisplayValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.Type) }}
            </span>
          </div>
          <div class="text-base-content/60 mt-1 flex min-w-0 items-center gap-2 text-xs">
            <span class="truncate">
              {{ getDisplayValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.Rule) }}
            </span>
            <span class="text-base-content/40 shrink-0">-></span>
            <span class="truncate">
              {{ getDisplayValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.Chains) }}
            </span>
          </div>
        </div>
        <div class="text-success flex shrink-0 items-center gap-1 text-sm font-semibold">
          <ArrowDownCircleIcon class="h-4 w-4" />
          {{ getDisplayValue(connection, CONNECTIONS_TABLE_ACCESSOR_KEY.DlSpeed) }}
        </div>
      </div>
    </template>
    <div
      v-else
      class="text-base-content/60 col-span-full row-span-4 flex h-full min-h-[232px] items-center justify-center text-sm"
    >
      暂无下载中的连接
    </div>
  </div>
</template>

<script setup lang="ts">
import { getConnectionDisplayValue } from '@/assembly/connections'
import { CONNECTIONS_TABLE_ACCESSOR_KEY } from '@/constant'
import { activeConnections } from '@/store/connections'
import { proxyChainDirection, showFullProxyChain } from '@/store/settings'
import type { Connection } from '@/types'
import { ArrowDownCircleIcon } from '@heroicons/vue/24/outline'
import { computed, ref, watch } from 'vue'

const displayOptions = computed(() => ({
  mode: 'card' as const,
  proxyChainDirection: proxyChainDirection.value,
  showFullProxyChain: showFullProxyChain.value,
}))

const displayConnections = ref<Connection[]>([])
const displayLimit = 4

const cloneConnection = (connection: Connection, downloadSpeed = connection.downloadSpeed) => ({
  ...connection,
  downloadSpeed,
})

watch(
  activeConnections,
  (connections) => {
    const downloadingConnections = connections
      .filter((connection) => connection.downloadSpeed > 0)
      .slice()
      .sort((a, b) => b.downloadSpeed - a.downloadSpeed)
      .slice(0, displayLimit)

    const retainedConnections = displayConnections.value.map((connection) => {
      const currentConnection = connections.find((item) => item.id === connection.id)

      return cloneConnection(currentConnection ?? connection, 0)
    })

    displayConnections.value = Array.from({ length: displayLimit }, (_, index) => {
      const downloadingConnection = downloadingConnections[index]
      if (downloadingConnection) {
        return cloneConnection(downloadingConnection)
      }

      return retainedConnections[index]
    }).filter((connection): connection is Connection => !!connection)
  },
  { immediate: true },
)

const getDisplayValue = (connection: Connection, key: CONNECTIONS_TABLE_ACCESSOR_KEY) => {
  return getConnectionDisplayValue(connection, key, displayOptions.value)
}
</script>
