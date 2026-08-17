<template>
  <div class="bg-base-200/30 flex flex-col rounded-xl p-4">
    <div class="flex items-center justify-between">
      <div class="text-base-content/60 text-xs font-semibold tracking-wider uppercase">
        {{ $t('networkInfo') }}
      </div>
      <div class="flex gap-1">
        <button
          class="btn btn-ghost btn-xs btn-circle"
          @click="showPrivacy = !showPrivacy"
          @mouseenter="handlerShowPrivacyTip"
        >
          <EyeIcon
            v-if="showPrivacy"
            class="h-3.5 w-3.5"
          />
          <EyeSlashIcon
            v-else
            class="h-3.5 w-3.5"
          />
        </button>
        <button
          class="btn btn-ghost btn-xs btn-circle"
          @click="getIPs"
        >
          <BoltIcon class="h-3.5 w-3.5" />
        </button>
      </div>
    </div>

    <div class="mt-3 flex flex-col gap-3">
      <!-- China IP -->
      <div>
        <div class="text-base-content/60 text-xs">ipip.net</div>
        <div class="mt-0.5 text-sm">
          {{ showPrivacy ? ipForChina.ipWithPrivacy[0] : ipForChina.ip[0] }}
          <span
            v-if="ipForChina.ip[1]"
            class="text-base-content/60 text-xs"
          >
            ({{ showPrivacy ? ipForChina.ipWithPrivacy[1] : ipForChina.ip[1] }})
          </span>
        </div>
      </div>

      <div class="border-base-content/5 border-t" />

      <!-- Global IP -->
      <div>
        <div class="text-base-content/60 text-xs">{{ IPInfoAPI }}</div>
        <div class="mt-0.5 text-sm">
          {{ showPrivacy ? ipForGlobal.ipWithPrivacy[0] : ipForGlobal.ip[0] }}
          <span
            v-if="ipForGlobal.ip[1]"
            class="text-base-content/60 text-xs"
          >
            ({{ showPrivacy ? ipForGlobal.ipWithPrivacy[1] : ipForGlobal.ip[1] }})
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { getIPFromIpipnetAPI, getIPInfo } from '@/api/geoip'
import { ipForChina, ipForGlobal } from '@/composables/overview'
import { useTooltip } from '@/helper/tooltip'
import { autoIPCheck, IPInfoAPI } from '@/store/settings'
import { BoltIcon, EyeIcon, EyeSlashIcon } from '@heroicons/vue/24/outline'
import { onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
const showPrivacy = ref(false)
const { showTip } = useTooltip()
const handlerShowPrivacyTip = (e: Event) => {
  showTip(e, t('ipScreenshotTip'))
}

const QUERYING_IP_INFO = {
  ip: [t('getting'), ''],
  ipWithPrivacy: [t('getting'), ''],
}

const FAILED_IP_INFO = {
  ip: [t('testFailed'), ''],
  ipWithPrivacy: [t('testFailed'), ''],
}

const getIPs = () => {
  ipForChina.value = {
    ...QUERYING_IP_INFO,
  }
  ipForGlobal.value = {
    ...QUERYING_IP_INFO,
  }
  getIPInfo()
    .then((res) => {
      ipForGlobal.value = {
        ipWithPrivacy: [`${res.country} ${res.organization}`, res.ip],
        ip: [`${res.country} ${res.organization}`, '***.***.***.***'],
      }
    })
    .catch(() => {
      ipForGlobal.value = {
        ...FAILED_IP_INFO,
      }
    })
  getIPFromIpipnetAPI()
    .then((res) => {
      ipForChina.value = {
        ipWithPrivacy: [res.data.location.join(' '), res.data.ip],
        ip: [`${res.data.location[0]} ** ** **`, '***.***.***.***'],
      }
    })
    .catch(() => {
      ipForChina.value = {
        ...FAILED_IP_INFO,
      }
    })
}

watch(IPInfoAPI, () => {
  if ([ipForChina, ipForGlobal].some((item) => item.value.ip.length !== 0)) {
    getIPs()
  }
})

onMounted(() => {
  if (autoIPCheck.value && [ipForChina, ipForGlobal].some((item) => item.value.ip.length === 0)) {
    getIPs()
  }
})
</script>
