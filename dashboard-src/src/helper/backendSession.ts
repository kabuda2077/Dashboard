import { hasHostBridge, hostSessionGeneration, hostState } from '@/composables/hostBridge'
import { activeBackend } from '@/store/setup'
import { computed, ref, watch } from 'vue'

const browserGeneration = ref(0)
if (!hasHostBridge)
  watch(
    () => {
      const backend = activeBackend.value
      return JSON.stringify([
        backend?.uuid,
        backend?.protocol,
        backend?.host,
        backend?.port,
        backend?.secondaryPath,
        backend?.password,
      ])
    },
    () => {
      browserGeneration.value++
    },
    { flush: 'sync' },
  )
export const backendSessionGeneration = computed(() =>
  hasHostBridge ? hostSessionGeneration.value : browserGeneration.value,
)
const unauthorizedGeneration = ref<number>()
export const markBackendUnauthorized = () => {
  unauthorizedGeneration.value = backendSessionGeneration.value
}
export const backendConnectionStatus = computed(() =>
  unauthorizedGeneration.value === backendSessionGeneration.value
    ? 'unauthorized'
    : hasHostBridge
      ? (hostState.value.apiStatus ?? 'idle')
      : activeBackend.value
        ? 'ready'
        : 'idle',
)
export const backendSessionReady = computed(
  () =>
    !!activeBackend.value &&
    backendConnectionStatus.value === 'ready' &&
    (!hasHostBridge || hostState.value.isRunning === true),
)
export const captureBackendSession = () => {
  const generation = backendSessionGeneration.value
  return { isCurrent: () => generation === backendSessionGeneration.value }
}
