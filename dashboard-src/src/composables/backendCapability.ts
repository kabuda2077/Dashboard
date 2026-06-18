import { getSingboxUrlFromBackend } from '@/helper/utils'
import { activeBackend } from '@/store/setup'
import { computed } from 'vue'

export const hasClashChannel = computed(() => !!activeBackend.value)
export const hasSingboxChannel = computed(
  () =>
    __SINGBOX_NATIVE__ &&
    !!activeBackend.value &&
    getSingboxUrlFromBackend(activeBackend.value) !== '',
)

export const isFusionBackend = computed(() => hasClashChannel.value && hasSingboxChannel.value)

export const capabilities = computed(() => ({
  proxies: hasClashChannel.value,
  connections: hasClashChannel.value,
  logs: hasClashChannel.value,
  overview: hasClashChannel.value,
  rules: hasClashChannel.value,
  providers: hasClashChannel.value,
  dns: hasClashChannel.value,
  smart: hasClashChannel.value,
  upgrade: hasClashChannel.value,
  tools: hasSingboxChannel.value,
}))
