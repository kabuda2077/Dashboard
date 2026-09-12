import type { IPInfo } from '@/api/geoip'
import { LANG } from '@/constant'
import { geoIPChunkStore, type GeoIPFileManifest } from '@/helper/geoipChunkStore'
import { AsyncMMDBReader } from '@/helper/mmdb'
import { geoipASNDatabaseURL, geoipCountryDatabaseURL, language } from '@/store/settings'
import { watchDebounced } from '@vueuse/core'
import * as ipaddr from 'ipaddr.js'
import type { AsnResponse, CountryResponse } from 'mmdb-lib'
import { reactive } from 'vue'

/**
 * Local GeoIP lookup backed by GeoIP databases (Country for the country, ASN for
 * the autonomous system / organization).
 *
 * This module is loaded only while the connections page is showing GeoIP. Each
 * database is downloaded once from the CDN and streamed into the shared GeoIP
 * IndexedDB chunk store. Lookups only read the MMDB tree/data chunks they touch.
 */
const GEOIP_DATABASE_TTL = 30 * 24 * 60 * 60 * 1000

type GeoIPResponse = CountryResponse | AsnResponse

const openReader = <T extends GeoIPResponse>(key: string, manifest: GeoIPFileManifest) =>
  AsyncMMDBReader.open<T>(geoIPChunkStore.createSource(key, manifest))

const loadReader = async (url: string): Promise<AsyncMMDBReader<GeoIPResponse>> => {
  let cached = await geoIPChunkStore.getManifest(url).catch(() => undefined)
  let staleReader: AsyncMMDBReader<GeoIPResponse> | undefined

  if (cached) {
    try {
      staleReader = await openReader<GeoIPResponse>(url, cached)

      if (Date.now() - cached.updatedAt <= GEOIP_DATABASE_TTL) return staleReader
    } catch {
      await geoIPChunkStore.invalidate(url, cached.generation).catch(() => {})
      cached = undefined
    }
  }

  try {
    const response = await fetch(url)

    if (!response.ok || !response.body) {
      throw new Error(`Failed to stream GeoIP database: ${response.status}`)
    }

    const staged = await geoIPChunkStore.stageStream(url, response.body)
    let nextReader: AsyncMMDBReader<GeoIPResponse>

    try {
      nextReader = await openReader<GeoIPResponse>(url, staged)
      // Country and ASN databases are small enough to retain one generation
      // for another open tab that may still hold an older Reader.
      await geoIPChunkStore.activate(url, staged, { retainPrevious: true })
    } catch (error) {
      await geoIPChunkStore.discard(url, staged.generation).catch(() => {})
      throw error
    }

    return nextReader
  } catch (error) {
    // A validated stale generation remains usable until the replacement has
    // been completely written, parsed and atomically activated.
    if (staleReader) return staleReader
    throw error
  }
}

// Only the active country + ASN readers stay reachable. Each reader has its own
// bounded chunk cache, so stale URL edits cannot multiply the memory ceiling.
const GEOIP_READER_CACHE_MAX = 2
const readerCache = new Map<string, Promise<AsyncMMDBReader<GeoIPResponse>>>()

const getReader = <T extends GeoIPResponse>(url: string): Promise<AsyncMMDBReader<T>> => {
  const cached = readerCache.get(url)

  if (cached) {
    // Mark as most-recently-used.
    readerCache.delete(url)
    readerCache.set(url, cached)

    return cached as Promise<AsyncMMDBReader<T>>
  }

  const reader = loadReader(url).catch((error) => {
    // Drop the failed entry so a later lookup can retry the download.
    readerCache.delete(url)
    throw error
  })

  readerCache.set(url, reader)

  // Evict the least-recently-used entries beyond the cap.
  while (readerCache.size > GEOIP_READER_CACHE_MAX) {
    const oldest = readerCache.keys().next().value

    if (oldest === undefined) {
      break
    }

    readerCache.delete(oldest)
  }

  return reader as Promise<AsyncMMDBReader<T>>
}

const localizedName = (names?: { en: string; 'zh-CN'?: string }): string => {
  if (!names) {
    return ''
  }

  const preferChinese = language.value === LANG.ZH_CN || language.value === LANG.ZH_TW

  return preferChinese ? (names['zh-CN'] ?? names.en) : names.en
}

// Look up a single IP. A failure to load the database propagates (so the caller
// can retry later); only a lookup miss / decode error for this IP becomes null.
const lookup = async <T extends GeoIPResponse>(url: string, ip: string): Promise<T | null> => {
  const reader = await getReader<T>(url)

  try {
    return await reader.get(ip)
  } catch {
    return null
  }
}

const getGeoIPInfo = async (ip: string): Promise<IPInfo> => {
  const [country, asn] = await Promise.all([
    lookup<CountryResponse>(geoipCountryDatabaseURL.value, ip),
    lookup<AsnResponse>(geoipASNDatabaseURL.value, ip),
  ])

  return {
    ip,
    // Real countries carry localized names; category ranges (e.g. GOOGLE) only
    // have an iso_code, so fall back to that.
    country: localizedName(country?.country?.names) || (country?.country?.iso_code ?? ''),
    region: '',
    city: '',
    asn: asn?.autonomous_system_number?.toString() ?? '',
    organization: asn?.autonomous_system_organization ?? '',
    latitude: null,
    longitude: null,
  }
}

const EMPTY_GEOIP_INFO: IPInfo = {
  ip: '',
  country: '',
  region: '',
  city: '',
  asn: '',
  organization: '',
  latitude: null,
  longitude: null,
}

// Cap the resolved-info cache; a session may touch many distinct IPs, and each
// entry is tiny, so this only guards against unbounded growth.
const GEOIP_INFO_CACHE_MAX = 4096
const geoInfoCache = reactive(new Map<string, IPInfo>())
const geoInfoPending = new Set<string>()

/**
 * Reactive, synchronous GeoIP lookup for render paths (e.g. table cells).
 *
 * Returns the cached info immediately, or empty values while the async lookup
 * runs in the background; once resolved the reactive cache updates and dependent
 * views re-render.
 */
export const getConnectionGeoIPInfoSync = (ip: string): IPInfo => {
  if (!ip || !ipaddr.isValid(ip)) {
    return EMPTY_GEOIP_INFO
  }

  const cached = geoInfoCache.get(ip)

  if (cached) {
    return cached
  }

  if (!geoInfoPending.has(ip)) {
    geoInfoPending.add(ip)
    getGeoIPInfo(ip)
      .then((info) => {
        geoInfoCache.set(ip, info)

        // Evict oldest entries beyond the cap (FIFO; safe here since this runs
        // in a microtask, not during a render read of the reactive cache).
        while (geoInfoCache.size > GEOIP_INFO_CACHE_MAX) {
          const oldest = geoInfoCache.keys().next().value

          if (oldest === undefined) {
            break
          }

          geoInfoCache.delete(oldest)
        }
      })
      .catch(() => {})
      .finally(() => geoInfoPending.delete(ip))
  }

  return EMPTY_GEOIP_INFO
}

// URL changes only invalidate local state. A new download still waits until a
// visible connection GeoIP cell asks for data.
watchDebounced(
  [geoipCountryDatabaseURL, geoipASNDatabaseURL],
  () => {
    readerCache.clear()
    geoInfoCache.clear()
    geoInfoPending.clear()
  },
  { debounce: 800 },
)
