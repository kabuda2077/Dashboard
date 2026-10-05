import { hasHostBridge, hostState } from '@/composables/hostBridge'
import type { Backend } from '@/types'
import { useStorage } from '@vueuse/core'
import { v4 as uuid } from 'uuid'
import { computed, ref } from 'vue'
import { sourceIPLabelList } from './settings'

// Browser configuration is not used as an intermediate copy of desktop host state.
export const backendList = hasHostBridge
  ? ref<Backend[]>([])
  : useStorage<Backend[]>('setup-v2/backends', [])
const browserUuid = hasHostBridge
  ? ref<string | null>('')
  : useStorage<string | null>('setup-v2/active', '')
export const activeUuid = computed<string | null>({
  get: () =>
    hasHostBridge
      ? hostState.value.coreType
        ? `desktop:${hostState.value.coreType}`
        : ''
      : browserUuid.value,
  set: (value) => {
    if (!hasHostBridge) browserUuid.value = value
  },
})

export const activeBackend = computed<Backend | undefined>((previous) => {
  if (!hasHostBridge) return backendList.value.find((backend) => backend.uuid === browserUuid.value)
  const state = hostState.value
  if (!state.coreType || !state.apiUrl || state.secretDecryptionFailed) return undefined
  try {
    const address = new URL(state.apiUrl)
    const next: Backend = {
      type: 'clash',
      uuid: `desktop:${state.coreType}`,
      protocol: address.protocol.slice(0, -1),
      host: address.hostname,
      port: address.port || (address.protocol === 'https:' ? '443' : '80'),
      secondaryPath: address.pathname.replace(/\/$/, ''),
      password: state.secret ?? '',
      label: state.coreTitle || state.coreType,
      disableUpgradeCore: true,
      readOnlyTunEnabled:
        typeof state.readOnlyTunEnabled === 'boolean' ? state.readOnlyTunEnabled : undefined,
    }
    return previous &&
      Object.keys(next).every(
        (key) => next[key as keyof Backend] === previous[key as keyof Backend],
      )
      ? previous
      : next
  } catch {
    return undefined
  }
})
export const showBackendSettingsDialog = ref(false)
export const toggleBackendSettingsDialog = () => {
  showBackendSettingsDialog.value = !showBackendSettingsDialog.value
}

export const switchActiveBackend = (direction: 1 | -1) => {
  if (hasHostBridge || backendList.value.length < 2) return null
  const index = Math.max(
    0,
    backendList.value.findIndex((item) => item.uuid === browserUuid.value),
  )
  const backend =
    backendList.value[(index + direction + backendList.value.length) % backendList.value.length]
  if (backend) browserUuid.value = backend.uuid
  return backend ?? null
}

const sameEndpoint = (left: Backend, right: Omit<Backend, 'uuid'>) =>
  left.protocol === right.protocol &&
  left.host === right.host &&
  left.port === right.port &&
  left.secondaryPath === right.secondaryPath &&
  left.password === right.password &&
  left.type === right.type

export const addBackend = (
  backend: Omit<Backend, 'uuid'>,
  options?: { replaceExisting?: boolean },
) => {
  if (hasHostBridge) return
  const existing = backendList.value.find((saved) => sameEndpoint(saved, backend))
  const next = { ...backend, uuid: existing?.uuid ?? uuid() }
  backendList.value = options?.replaceExisting
    ? [next]
    : existing
      ? backendList.value.map((saved) => (saved.uuid === existing.uuid ? next : saved))
      : [...backendList.value, next]
  browserUuid.value = next.uuid
}
export const updateBackend = (id: string, backend: Omit<Backend, 'uuid'>) => {
  if (!hasHostBridge)
    backendList.value = backendList.value.map((item) =>
      item.uuid === id ? { ...backend, uuid: id } : item,
    )
}
export const removeBackend = (id: string) => {
  if (hasHostBridge) return
  backendList.value = backendList.value.filter((item) => item.uuid !== id)
  if (browserUuid.value === id) browserUuid.value = backendList.value[0]?.uuid ?? ''
  sourceIPLabelList.value.forEach((label) => {
    if (label.scope?.includes(id)) {
      label.scope = label.scope.filter((scope) => scope !== id)
      if (!label.scope.length) delete label.scope
    }
  })
}
