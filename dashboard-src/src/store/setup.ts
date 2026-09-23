import type { Backend } from '@/types'
import { useStorage } from '@vueuse/core'
import { omit } from 'lodash'
import { v4 as uuid } from 'uuid'
import { computed, ref } from 'vue'
import { sourceIPLabelList } from './settings'
import { hasHostBridge, hostState } from '@/composables/hostBridge'

// 清理旧版本留下的 native 后端；桌面版只保留 Clash-compatible API。
type LegacyBackend = Omit<Partial<Backend>, 'type'> & {
  type?: string
  singboxChannel?: unknown
}

const migrateBackendList = (list: LegacyBackend[]): Backend[] => {
  return list
    .filter((item) => item.type !== 'singbox')
    .map((item) => ({
      ...(omit(item, 'singboxChannel') as Backend),
      type: 'clash',
    }))
}

// Desktop credentials live only in memory. Persist endpoint identity so history
// and label scopes retain their UUID across WebView recreation and app upgrades.
const desktopBackendSerializer = {
  read: (raw: string): Backend[] => JSON.parse(raw).map((backend: Backend) => ({ ...backend, password: '' })),
  write: (backends: Backend[]) => JSON.stringify(backends.map(({ password: _password, ...backend }) => backend)),
}
if (hasHostBridge) {
  const legacy = localStorage.getItem('setup/api-list')
  if (legacy) {
    // No backup copy: the host settings file remains the credential authority.
    // Malformed data is retained for recovery; it is never used as credentials.
    try {
      localStorage.setItem('setup/api-list', desktopBackendSerializer.write(desktopBackendSerializer.read(legacy)))
    } catch { /* VueUse falls back to defaults if the old list is unreadable. */ }
  }
}
export const backendList = useStorage<Backend[]>('setup/api-list', [], undefined,
  hasHostBridge ? { serializer: desktopBackendSerializer } : undefined)
export const activeUuid = useStorage<string>('setup/active-uuid', '')

const storedBackends = backendList.value as LegacyBackend[]
if (storedBackends.some((item) => item.type !== 'clash' || 'singboxChannel' in item)) {
  backendList.value = migrateBackendList(storedBackends)
  if (!backendList.value.some((backend) => backend.uuid === activeUuid.value)) {
    activeUuid.value = backendList.value[0]?.uuid || ''
  }
}

export const showBackendSettingsDialog = ref(false)

export const toggleBackendSettingsDialog = () => {
  showBackendSettingsDialog.value = !showBackendSettingsDialog.value
}
export const activeBackend = computed(() => {
  if (hasHostBridge && (!hostState.value.apiUrl || hostState.value.secretDecryptionFailed)) return undefined
  return backendList.value.find((backend) => backend.uuid === activeUuid.value)
})

export const switchActiveBackend = (direction: 1 | -1) => {
  if (backendList.value.length < 2) {
    return null
  }

  const currentIndex = backendList.value.findIndex((backend) => backend.uuid === activeUuid.value)
  const startIndex = currentIndex >= 0 ? currentIndex : 0
  const nextIndex = (startIndex + direction + backendList.value.length) % backendList.value.length

  const nextBackend = backendList.value[nextIndex]

  if (!nextBackend) {
    return null
  }

  activeUuid.value = nextBackend.uuid
  return nextBackend
}

const isSameBackendEndpoint = (saved: Backend, backend: Omit<Backend, 'uuid'>) => {
  return (
    saved.protocol === backend.protocol &&
    saved.host === backend.host &&
    saved.port === backend.port &&
    saved.secondaryPath === backend.secondaryPath &&
    saved.password === backend.password &&
    saved.type === backend.type
  )
}

export const addBackend = (
  backend: Omit<Backend, 'uuid'>,
  options?: {
    replaceExisting?: boolean
  },
) => {
  const matchingBackends = backendList.value.filter((end) => isSameBackendEndpoint(end, backend))
  const currentEnd = matchingBackends[0] ?? (options?.replaceExisting
    ? backendList.value.find((saved) => (saved.uuid === activeUuid.value || hasHostBridge)
      && saved.protocol === backend.protocol && saved.host === backend.host
      && saved.port === backend.port && saved.secondaryPath === backend.secondaryPath
      && saved.type === backend.type)
    : undefined)

  if (currentEnd) {
    if (options?.replaceExisting) {
      backendList.value = [{
        ...backend,
        uuid: currentEnd.uuid,
      }]
    } else if (matchingBackends.length > 1) {
      Object.assign(currentEnd, backend)
      const duplicateIds = new Set(matchingBackends.slice(1).map((end) => end.uuid))
      backendList.value = backendList.value.filter((end) => !duplicateIds.has(end.uuid))
    } else {
      Object.assign(currentEnd, backend)
    }
    activeUuid.value = currentEnd.uuid
    return
  }

  const id = uuid()

  const nextBackend = {
    ...backend,
    uuid: id,
  }

  if (options?.replaceExisting) {
    backendList.value = [nextBackend]
  } else {
    backendList.value.push(nextBackend)
  }
  activeUuid.value = id
}

export const updateBackend = (uuid: string, backend: Omit<Backend, 'uuid'>) => {
  const index = backendList.value.findIndex((end) => end.uuid === uuid)
  if (index !== -1) {
    backendList.value[index] = {
      ...backend,
      uuid,
    }
  }
}

export const removeBackend = (uuid: string) => {
  backendList.value = backendList.value.filter((end) => end.uuid !== uuid)
  sourceIPLabelList.value.forEach((label) => {
    if (label.scope && label.scope.includes(uuid)) {
      label.scope = label.scope.filter((scope) => scope !== uuid)
      if (!label.scope.length) {
        delete label.scope
      }
    }
  })
}
