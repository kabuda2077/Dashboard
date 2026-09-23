import { hasHostBridge, hostSessionGeneration, hostState } from '@/composables/hostBridge'
import { activeBackend } from '@/store/setup'
import { computed, readonly, ref, watch } from 'vue'

// One identity for both desktop runtime changes and browser connection edits.
// Synchronous invalidation protects replies arriving before Vue renders the new session.
const generation = ref(0)
const signature = computed(() => {
  const backend = activeBackend.value
  return JSON.stringify([backend?.uuid, backend?.protocol, backend?.host, backend?.port,
    backend?.secondaryPath, backend?.password, hostSessionGeneration.value])
})
watch(signature, () => { generation.value++ }, { flush: 'sync' })

export const backendSessionGeneration = readonly(generation)
export const backendSessionReady = computed(() => !!activeBackend.value
  && (!hasHostBridge || hostState.value.isRunning !== false))

export const captureBackendSession = () => {
  const captured = generation.value
  return { isCurrent: () => captured === generation.value }
}
