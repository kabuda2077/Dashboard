import { IP_INFO_API, LANG } from '@/constant'
import { geoipASNDatabaseURL, geoipCountryDatabaseURL, IPInfoAPI, language } from '@/store/settings'
import { watchDebounced } from '@vueuse/core'
import { Buffer } from 'buffer'
import * as ipaddr from 'ipaddr.js'
import type { AsnResponse, CountryResponse, Reader } from 'mmdb-lib'
import { reactive } from 'vue'

if (!(globalThis as { Buffer?: unknown }).Buffer) {
  ;(globalThis as { Buffer?: unknown }).Buffer = Buffer
}

export interface IPInfo {
  ip: string
  country: string
  region: string
  city: string
  asn: string
  organization: string
}

// china
export const getIPFromIpipnetAPI = async () => {
  // Query-string cache busting can make ipip.net omit its CORS header.
  const response = await fetch('https://myip.ipip.net/json', { cache: 'no-store' })

  return (await response.json()) as {
    data: {
      ip: string
      location: string[]
    }
  }
}

// global
const getIPFromIpsbAPI = async (ip = '') => {
  const response = await fetch('https://api.ip.sb/geoip' + (ip ? `/${ip}` : ''), {
    cache: 'no-store',
  })

  return (await response.json()) as {
    organization: string
    longitude: number
    city: string
    region: string
    timezone: string
    isp: string
    offset: number
    asn: number
    asn_organization: string
    country: string
    ip: string
    latitude: number
    postal_code: string
    continent_code: string
    country_code: string
    region_code: string
  }
}

const getIPFromIPWhoisAPI = async (ip = '') => {
  const response = await fetch('https://ipwho.is' + (ip ? `/${ip}` : ''), {
    cache: 'no-store',
  })

  return (await response.json()) as {
    ip: string
    success: boolean
    type: string
    continent: string
    continent_code: string
    country: string
    country_code: string
    region: string
    region_code: string
    city: string
    latitude: number
    longitude: number
    is_eu: boolean
    postal: string
    calling_code: string
    capital: string
    borders: string
    flag: {
      img: string
      emoji: string
      emoji_unicode: string
    }
    connection: {
      asn: number
      org: string
      isp: string
      domain: string
    }
    timezone: {
      id: string
      abbr: string
      is_dst: boolean
      offset: number
      utc: string
      current_time: string
    }
  }
}

const getIPFromIPapiisAPI = async (ip = '') => {
  const response = await fetch('https://api.ipapi.is' + (ip ? `/?q=${ip}` : ''), {
    cache: 'no-store',
  })

  return (await response.json()) as {
    ip: string
    company_name?: string | null
    asn_num?: number | null
    asn_org?: string | null
    cc?: string | null
    // Keep compatibility with older ipapi.is responses.
    asn?: { asn?: number; org?: string }
    location?: { country?: string; state?: string; city?: string }
  }
}

export const getIPInfo = async (ip = ''): Promise<IPInfo> => {
  switch (IPInfoAPI.value) {
    case IP_INFO_API.IPAPI:
      const ipapi = await getIPFromIPapiisAPI(ip)

      return {
        ip: ipapi.ip,
        country: ipapi.cc ?? ipapi.location?.country ?? '',
        region: ipapi.location?.state ?? '',
        city: ipapi.location?.city ?? '',
        asn: (ipapi.asn_num ?? ipapi.asn?.asn)?.toString() ?? '',
        organization: ipapi.asn_org ?? ipapi.asn?.org ?? ipapi.company_name ?? '',
      }
    case IP_INFO_API.IPWHOIS:
      const ipwhois = await getIPFromIPWhoisAPI(ip)

      return {
        ip: ipwhois.ip,
        region: ipwhois.region,
        country: ipwhois.country,
        city: ipwhois.city,
        asn: ipwhois.connection.asn?.toString(),
        organization: ipwhois.connection.org,
      }
    case IP_INFO_API.IPSB:
    default:
      const ipsb = await getIPFromIpsbAPI(ip)

      return {
        ip: ipsb.ip,
        country: ipsb.country,
        region: ipsb.region,
        city: ipsb.city,
        asn: ipsb.asn?.toString(),
        organization: ipsb.organization,
      }
  }
}

const GEOIP_IDB_NAME = 'zashboard-geoip'
const GEOIP_IDB_STORE = 'mmdb'
const GEOIP_DATABASE_TTL = 30 * 24 * 60 * 60 * 1000

interface CachedGeoIPDatabase {
  buffer: ArrayBuffer
  updatedAt: number
}

const openGeoIPDB = (): Promise<IDBDatabase> =>
  new Promise((resolve, reject) => {
    const request = indexedDB.open(GEOIP_IDB_NAME, 1)

    request.onupgradeneeded = () => {
      request.result.createObjectStore(GEOIP_IDB_STORE)
    }
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error)
  })

const readCachedDatabase = async (key: string): Promise<CachedGeoIPDatabase | undefined> => {
  const db = await openGeoIPDB()

  return new Promise((resolve, reject) => {
    const request = db
      .transaction(GEOIP_IDB_STORE, 'readonly')
      .objectStore(GEOIP_IDB_STORE)
      .get(key)

    request.onsuccess = () => resolve(request.result as CachedGeoIPDatabase | undefined)
    request.onerror = () => reject(request.error)
  }).finally(() => db.close()) as Promise<CachedGeoIPDatabase | undefined>
}

const writeCachedDatabase = async (key: string, value: CachedGeoIPDatabase): Promise<void> => {
  const db = await openGeoIPDB()

  return new Promise<void>((resolve, reject) => {
    const transaction = db.transaction(GEOIP_IDB_STORE, 'readwrite')

    transaction.objectStore(GEOIP_IDB_STORE).put(value, key)
    transaction.oncomplete = () => resolve()
    transaction.onerror = () => reject(transaction.error)
  }).finally(() => db.close())
}

type GeoIPResponse = CountryResponse | AsnResponse

const loadReader = async (url: string): Promise<Reader<GeoIPResponse>> => {
  let cached = await readCachedDatabase(url).catch(() => undefined)

  if (!cached || Date.now() - cached.updatedAt > GEOIP_DATABASE_TTL) {
    try {
      const response = await fetch(url)

      if (!response.ok) {
        throw new Error(`Failed to download GeoIP database: ${response.status}`)
      }

      cached = { buffer: await response.arrayBuffer(), updatedAt: Date.now() }
      await writeCachedDatabase(url, cached).catch(() => {})
    } catch (error) {
      if (!cached) {
        throw error
      }
    }
  }

  const { Reader } = await import('mmdb-lib')

  return new Reader<GeoIPResponse>(Buffer.from(cached.buffer))
}

const GEOIP_READER_CACHE_MAX = 4
const readerCache = new Map<string, Promise<Reader<GeoIPResponse>>>()

const getReader = <T extends GeoIPResponse>(url: string): Promise<Reader<T>> => {
  const cached = readerCache.get(url)

  if (cached) {
    readerCache.delete(url)
    readerCache.set(url, cached)

    return cached as Promise<Reader<T>>
  }

  const reader = loadReader(url).catch((error) => {
    readerCache.delete(url)
    throw error
  })

  readerCache.set(url, reader)

  while (readerCache.size > GEOIP_READER_CACHE_MAX) {
    const oldest = readerCache.keys().next().value

    if (oldest === undefined) {
      break
    }

    readerCache.delete(oldest)
  }

  return reader as Promise<Reader<T>>
}

const localizedName = (names?: { en: string; 'zh-CN'?: string }): string => {
  if (!names) {
    return ''
  }

  const preferChinese = language.value === LANG.ZH_CN || language.value === LANG.ZH_TW

  return preferChinese ? (names['zh-CN'] ?? names.en) : names.en
}

const lookup = async <T extends GeoIPResponse>(url: string, ip: string): Promise<T | null> => {
  const reader = await getReader<T>(url)

  try {
    return reader.get(ip)
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
    country: localizedName(country?.country?.names) || (country?.country?.iso_code ?? ''),
    region: '',
    city: '',
    asn: asn?.autonomous_system_number?.toString() ?? '',
    organization: asn?.autonomous_system_organization ?? '',
  }
}

const EMPTY_GEOIP_INFO: IPInfo = {
  ip: '',
  country: '',
  region: '',
  city: '',
  asn: '',
  organization: '',
}

const GEOIP_INFO_CACHE_MAX = 4096
const geoInfoCache = reactive(new Map<string, IPInfo>())
const geoInfoPending = new Set<string>()

export const getGeoIPInfoSync = (ip: string): IPInfo => {
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

watchDebounced(
  [geoipCountryDatabaseURL, geoipASNDatabaseURL],
  () => {
    readerCache.clear()
    geoInfoCache.clear()
    geoInfoPending.clear()
  },
  { debounce: 800 },
)
