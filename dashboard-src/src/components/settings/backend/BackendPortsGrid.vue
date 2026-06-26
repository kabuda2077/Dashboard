<template>
  <div class="grid min-h-[5.5rem] w-full items-center gap-2">
    <div class="grid grid-cols-2 gap-2 md:grid-cols-5">
      <div
        v-for="port in ports"
        :key="port.key"
        class="rounded-box bg-base-200/70 grid gap-1 p-2"
      >
        <label
          :for="`port-${port.key}`"
          class="text-base-content/60 truncate text-xs font-semibold"
        >
          {{ $t(port.label) }}
        </label>
        <input
          :id="`port-${port.key}`"
          :value="configs?.[port.key] ?? ''"
          class="input input-sm bg-base-200/70 text-base-content border-transparent text-center shadow-none"
          type="number"
          inputmode="numeric"
          min="0"
          max="65535"
          @change="handleChange(port.key, $event)"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { configs, updateConfigs } from '@/assembly/config'

type PortKey = 'mixed-port' | 'port' | 'socks-port' | 'redir-port' | 'tproxy-port'

type PortItem = {
  label: string
  key: PortKey
}

const ports: PortItem[] = [
  {
    label: 'mixedPort',
    key: 'mixed-port',
  },
  {
    label: 'httpPort',
    key: 'port',
  },
  {
    label: 'socksPort',
    key: 'socks-port',
  },
  {
    label: 'redirPort',
    key: 'redir-port',
  },
  {
    label: 'tproxyPort',
    key: 'tproxy-port',
  },
]

const handleChange = (key: PortKey, event: Event) => {
  const value = Number((event.target as HTMLInputElement).value)
  updateConfigs({ [key]: Number.isNaN(value) ? 0 : value })
}
</script>
