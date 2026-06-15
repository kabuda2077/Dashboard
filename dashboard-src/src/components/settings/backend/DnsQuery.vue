<template>
  <div class="flex w-full flex-col gap-3">
    <form
      class="join w-96 max-w-full max-sm:w-full"
      @submit.prevent="query"
    >
      <TextInput
        v-model="form.name"
        placeholder="Domain Name"
        input-class="bg-base-200/60 border-transparent shadow-none focus:border-transparent"
        :clearable="true"
        :menus="dnsQueryNameHistory"
        :menus-deleteable="true"
        @update:menus="updateDnsQueryNameHistory"
      />
      <TextInput
        v-model="form.type"
        class="w-28"
        placeholder="Type"
        input-class="bg-base-200/60 border-transparent shadow-none focus:border-transparent"
        :menus="['A', 'AAAA', 'HTTPS']"
      />
      <button
        type="submit"
        class="btn join-item btn-sm bg-base-200/70 hover:bg-base-200/80 border-transparent shadow-none"
      >
        {{ $t('DNSQuery') }}
      </button>
    </form>
    <div
      v-if="resultList?.length"
      class="max-h-96 overflow-y-auto"
    >
      <div
        v-for="(item, index) in resultList"
        :key="`${item.name}-${item.type}-${item.data}-${index}`"
        class="btn btn-sm rounded-box bg-base-200/60 pointer-events-none h-auto min-h-10 w-full justify-between border-none px-3 py-2 font-normal shadow-none not-last:mb-2"
      >
        <div class="flex min-w-0 flex-1 items-center gap-2">
          <span
            class="bg-base-100/70 text-base-content/60 flex h-6 min-w-6 shrink-0 items-center justify-center rounded-full border border-transparent px-2 text-[11px] leading-none font-medium"
          >
            {{ getDnsTypeLabel(item.type) }}
          </span>
          <span class="text-base-content truncate text-sm leading-5 font-medium">
            {{ formatDnsName(item.name) }}
          </span>
          <span class="text-base-content/60 shrink-0 text-xs leading-5 font-normal">
            TTL {{ item.TTL }}
          </span>
        </div>
        <div
          class="text-base-content max-w-[50%] shrink-0 text-right text-sm leading-5 font-medium break-all"
        >
          {{ item.data }}
        </div>
      </div>
    </div>
    <div
      v-if="details"
      class="text-base-content/60 flex flex-wrap gap-x-3 gap-y-1 text-xs"
    >
      <div
        v-if="details?.country"
        class="mr-3 flex items-center gap-1"
      >
        <MapPinIcon class="h-4 w-4 shrink-0" />
        <template v-if="details?.city && details?.city !== details?.country">
          {{ details?.city }},
        </template>
        <template v-else-if="details?.region && details?.region !== details?.country">
          {{ details?.region }},
        </template>
        {{ details?.country }}
      </div>
      <div class="flex items-center gap-1">
        <ServerIcon class="h-4 w-4 shrink-0" />
        {{ details?.organization }}
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { queryDNSAPI } from '@/api'
import { getIPInfo, type IPInfo } from '@/api/geoip'
import type { DNSQuery } from '@/types'
import { MapPinIcon, ServerIcon } from '@heroicons/vue/24/outline'
import { useStorage } from '@vueuse/core'
import { reactive, ref } from 'vue'
import TextInput from '../../common/TextInput.vue'

const DNS_TYPE_LABELS: Record<number, string> = {
  1: 'A',
  5: 'CNAME',
  28: 'AAAA',
  65: 'HTTPS',
}

const form = reactive({
  name: 'www.google.com',
  type: 'A',
})
const details = ref<IPInfo | null>(null)
const resultList = ref<DNSQuery['Answer']>([])
const dnsQueryNameHistory = useStorage<string[]>('cache/dns-query-name-history', [])
const getDnsTypeLabel = (type: number) => DNS_TYPE_LABELS[type] ?? `TYPE ${type}`
const formatDnsName = (name: string) => name.replace(/\.$/, '')
const updateDnsQueryNameHistory = (history: string[]) => {
  dnsQueryNameHistory.value = history
}

const saveQueryName = (name: string) => {
  const queryName = name.trim()

  if (!queryName) {
    return
  }

  const nextHistory = dnsQueryNameHistory.value.filter((item) => item !== queryName)

  nextHistory.unshift(queryName)
  dnsQueryNameHistory.value = nextHistory.slice(0, 8)
}

const query = async () => {
  saveQueryName(form.name)

  const { data } = await queryDNSAPI(form)

  resultList.value = data.Answer

  const ipAnswer = resultList.value?.find(({ type }) => type === 1 || type === 28)

  if (ipAnswer) {
    details.value = await getIPInfo(ipAnswer.data)
  } else {
    details.value = null
  }
}
</script>
