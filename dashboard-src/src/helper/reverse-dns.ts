import { queryDNS } from '@/assembly/config'
import {
  backendSessionGeneration,
  backendSessionReady,
  captureBackendSession,
} from '@/helper/backendSession'
import { resolveClientHostname } from '@/store/settings'
import { activeBackend } from '@/store/setup'
import * as ipaddr from 'ipaddr.js'
import { ref, watch } from 'vue'

interface HostnameEntry {
  name: string | null
  ts: number
}

const POSITIVE_TTL = 45 * 60 * 1000
const NEGATIVE_TTL = 10 * 60 * 1000
const CACHE_KEY = 'cache/reverse-dns-hostnames'
const CACHE_CAP = 200
const MAX_INFLIGHT = 8
const memoryCache = new Map<string, HostnameEntry>()
const inflight = new Map<string, AbortController>()
export const reverseDNSRevision = ref(0)
let generation = 0
let unsupported = false

const parseIP = (ip: string): ipaddr.IPv4 | ipaddr.IPv6 | null => {
  try {
    const address = ipaddr.parse(ip.split('%')[0]!)
    return address.kind() === 'ipv6' && (address as ipaddr.IPv6).isIPv4MappedAddress()
      ? (address as ipaddr.IPv6).toIPv4Address()
      : address
  } catch {
    return null
  }
}

export const isResolvableIP = (ip: string | undefined) => {
  const address = ip ? parseIP(ip) : null
  return !!address && !['unspecified', 'loopback'].includes(address.range())
}

export const reverseName = (ip: string): string | null => {
  const address = parseIP(ip)
  if (!address) return null
  if (address.kind() === 'ipv4')
    return `${[...(address as ipaddr.IPv4).octets].reverse().join('.')}.in-addr.arpa`
  const nibbles = address
    .toByteArray()
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('')
  return `${nibbles.split('').reverse().join('.')}.ip6.arpa`
}

const expired = (entry: HostnameEntry, now = Date.now()) =>
  now - entry.ts > (entry.name === null ? NEGATIVE_TTL : POSITIVE_TTL)

// Include the endpoint as well as UUID: editing a profile must not reuse another
// core's names. Credentials never form part of persisted keys or values.
const cacheScope = () => {
  const backend = activeBackend.value
  return backend
    ? JSON.stringify([
        backend.uuid,
        backend.protocol,
        backend.host,
        backend.port,
        backend.secondaryPath,
      ])
    : ''
}

const readPersistedCache = (): Record<string, HostnameEntry> => {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(CACHE_KEY) || '{}')
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {}
    return Object.fromEntries(
      Object.entries(value)
        .filter(
          ([, entry]) =>
            entry &&
            typeof entry.name === 'string' &&
            entry.name.length > 0 &&
            entry.name.length <= 253 &&
            Number.isFinite(entry.ts) &&
            entry.ts <= Date.now() &&
            !expired(entry),
        )
        .sort(([, a], [, b]) => a.ts - b.ts)
        .slice(-CACHE_CAP),
    )
  } catch {
    return {}
  }
}

let persistedCache: Record<string, HostnameEntry> | undefined
const persistedEntries = () => (persistedCache ??= readPersistedCache())

const persistPositive = (key: string, entry: HostnameEntry) => {
  try {
    const cache = persistedEntries()
    cache[key] = entry
    persistedCache = Object.fromEntries(
      Object.entries(cache)
        .filter(([, value]) => !expired(value))
        .sort(([, a], [, b]) => a.ts - b.ts)
        .slice(-CACHE_CAP),
    )
    localStorage.setItem(CACHE_KEY, JSON.stringify(persistedCache))
  } catch {
    /* Storage failure does not prevent displaying a successful lookup. */
  }
}

watch(
  () => [
    backendSessionGeneration.value,
    backendSessionReady.value,
    resolveClientHostname.value,
    cacheScope(),
  ],
  () => {
    generation++
    for (const controller of inflight.values()) controller.abort()
    inflight.clear()
    memoryCache.clear()
    persistedCache = undefined
    unsupported = false
    reverseDNSRevision.value++
  },
  { flush: 'sync' },
)

export const getReverseDNSHostname = (ip: string | undefined): string | undefined => {
  void reverseDNSRevision.value
  if (
    !resolveClientHostname.value ||
    !backendSessionReady.value ||
    unsupported ||
    !isResolvableIP(ip)
  )
    return
  const scope = cacheScope()
  if (!scope || !ip) return
  const key = `${scope}\u0000${ip}`
  const cached = memoryCache.get(key)
  if (cached && !expired(cached)) return cached.name ?? undefined
  memoryCache.delete(key)
  const persisted = persistedEntries()[key]
  if (persisted && !expired(persisted)) {
    if (memoryCache.size + inflight.size < CACHE_CAP) memoryCache.set(key, persisted)
    return persisted.name!
  }
  if (inflight.has(key) || inflight.size >= MAX_INFLIGHT) return
  // Capacity is an admission limit until entries expire. Evicting a live miss
  // would make every reactive scan query the evicted address again.
  if (memoryCache.size + inflight.size >= CACHE_CAP) {
    for (const [cacheKey, entry] of memoryCache) {
      if (expired(entry)) memoryCache.delete(cacheKey)
    }
    if (memoryCache.size + inflight.size >= CACHE_CAP) return
  }

  const controller = new AbortController()
  const session = captureBackendSession()
  const requestedGeneration = generation
  const current = () =>
    session.isCurrent() && requestedGeneration === generation && !controller.signal.aborted
  inflight.set(key, controller)
  // Defer the API call: accessors remain synchronous and do not write reactive
  // state while connection rendering/search computations are being evaluated.
  void Promise.resolve()
    .then(async () => {
      if (!current()) return
      let name: string | null = null
      try {
        const result = await queryDNS({ name: reverseName(ip)!, type: 'PTR' }, controller.signal)
        if (!current()) return
        const answer = result?.Answer?.find((item) => item.type === 12)?.data
        if (typeof answer === 'string') {
          const candidate = answer.trim().replace(/\.$/, '')
          if (candidate.length > 0 && candidate.length <= 253) name = candidate
        }
      } catch (error) {
        if (!current()) return
        if (
          [404, 405, 501].includes(
            (error as { response?: { status?: number } }).response?.status ?? 0,
          )
        )
          unsupported = true
      }
      if (!current()) return
      const entry = { name, ts: Date.now() }
      memoryCache.set(key, entry)
      if (name) persistPositive(key, entry)
    })
    .finally(() => {
      if (inflight.get(key) === controller) {
        inflight.delete(key)
        reverseDNSRevision.value++
      }
    })
  return
}
