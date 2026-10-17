import { getConnectionGeoIPInfoSync } from '@/api/connection-geoip'
import { IP_INFO_API } from '@/constant'
import { IPInfoAPI } from '@/store/settings'

export interface IPInfo {
  ip: string
  country: string
  region: string
  city: string
  asn: string
  organization: string
  asnOrganization?: string
  latitude: number | null
  longitude: number | null
}

const coordinate = (value: unknown) =>
  typeof value === 'number' && Number.isFinite(value) ? value : null

const ensureResponseOK = (response: Response, service: string) => {
  if (!response.ok) throw new Error(`${service} lookup failed: ${response.status}`)
}

// Bound both the request and body read; existing callers need not provide a signal.
const fetchIPResource = async <T>(
  url: string,
  service: string,
  read: (response: Response) => Promise<T>,
  signal?: AbortSignal,
): Promise<T> => {
  const controller = new AbortController()
  const abort = () => controller.abort()
  const timeout = setTimeout(abort, 8000)
  signal?.addEventListener('abort', abort, { once: true })
  if (signal?.aborted) abort()
  try {
    const response = await fetch(url, {
      cache: 'no-store',
      credentials: 'omit',
      referrerPolicy: 'no-referrer',
      signal: controller.signal,
    })
    ensureResponseOK(response, service)
    return await read(response)
  } finally {
    clearTimeout(timeout)
    signal?.removeEventListener('abort', abort)
  }
}

// Observe the route chosen for the original hostname; do not force an address family.
export const getIPFromCloudflareAPI = async (signal?: AbortSignal) => {
  const trace = await fetchIPResource(
    'https://www.cloudflare.com/cdn-cgi/trace',
    'Cloudflare',
    (response) => response.text(),
    signal,
  )
  const fields = new Map(
    trace.split(/\r?\n/).map((line) => {
      const separator = line.indexOf('=')
      return [line.slice(0, separator), line.slice(separator + 1).trim()]
    }),
  )
  return { ip: fields.get('ip') ?? '', loc: fields.get('loc') ?? '' }
}

// China public-IP service. Query-string cache busting can make ipip.net omit CORS headers.
export const getIPFromIpipnetAPI = async (signal?: AbortSignal) => {
  return (await fetchIPResource(
    'https://myip.ipip.net/json',
    'ipip.net',
    (response) => response.json(),
    signal,
  )) as {
    ret?: string
    data: {
      ip: string
      location: string[]
    }
  }
}

export const getIPFromIpsbAPI = async (ip = '', signal?: AbortSignal) => {
  return (await fetchIPResource(
    'https://api.ip.sb/geoip' + (ip ? `/${ip}` : ''),
    IP_INFO_API.IPSB,
    (response) => response.json(),
    signal,
  )) as {
    ip: string
    organization?: string
    asn_organization?: string
    asn?: number
    country?: string
    region?: string
    city?: string
    latitude?: number
    longitude?: number
  }
}

const getIPFromIPWhoisAPI = async (ip = '', signal?: AbortSignal) => {
  return (await fetchIPResource(
    'https://ipwho.is' + (ip ? `/${ip}` : ''),
    IP_INFO_API.IPWHOIS,
    (response) => response.json(),
    signal,
  )) as
    | {
        ip: string
        success: true
        country?: string
        region?: string
        city?: string
        latitude?: number
        longitude?: number
        connection?: { asn?: number; org?: string; isp?: string }
      }
    | { ip?: string; success: false; message: string }
}

const getIPFromIPapiisAPI = async (ip = '', signal?: AbortSignal) => {
  return (await fetchIPResource(
    'https://api.ipapi.is' + (ip ? `/?q=${ip}` : ''),
    IP_INFO_API.IPAPI,
    (response) => response.json(),
    signal,
  )) as
    | {
        ip: string
        company?: string | { name?: string } | null
        asn?: string | { asn?: number; org?: string } | null
        country?: string
        region?: string
        city?: string
        lat?: number
        lon?: number
        location?: {
          country?: string
          state?: string
          city?: string
          latitude?: number
          longitude?: number
        }
      }
    | { error: string }
}

export const getIPInfo = async (
  ip = '',
  api: IP_INFO_API = IPInfoAPI.value as IP_INFO_API,
  signal?: AbortSignal,
): Promise<IPInfo> => {
  switch (api) {
    case IP_INFO_API.IPAPI: {
      const result = await getIPFromIPapiisAPI(ip, signal)
      if ('error' in result) throw new Error(`ipapi.is lookup failed: ${result.error}`)

      const flatASN = typeof result.asn === 'string' ? result.asn.trim().split(/\s+/) : undefined
      const asn = flatASN
        ? flatASN[0].replace(/^AS/i, '')
        : typeof result.asn === 'object'
          ? (result.asn?.asn?.toString() ?? '')
          : ''
      const asnOrganization = flatASN
        ? flatASN.slice(1).join(' ') || undefined
        : typeof result.asn === 'object'
          ? result.asn?.org
          : undefined

      return {
        ip: result.ip,
        country: result.location?.country ?? result.country ?? '',
        region: result.location?.state ?? result.region ?? '',
        city: result.location?.city ?? result.city ?? '',
        asn,
        asnOrganization,
        organization:
          typeof result.company === 'string'
            ? result.company
            : (result.company?.name ??
              (typeof result.asn === 'object' ? (result.asn?.org ?? '') : '')),
        latitude: coordinate(result.location?.latitude ?? result.lat),
        longitude: coordinate(result.location?.longitude ?? result.lon),
      }
    }
    case IP_INFO_API.IPWHOIS: {
      const result = await getIPFromIPWhoisAPI(ip, signal)
      if (!result.success) throw new Error(`IPWhois lookup failed: ${result.message}`)

      return {
        ip: result.ip,
        country: result.country ?? '',
        region: result.region ?? '',
        city: result.city ?? '',
        asn: result.connection?.asn?.toString() ?? '',
        asnOrganization: result.connection?.org,
        organization: result.connection?.isp || result.connection?.org || '',
        latitude: coordinate(result.latitude),
        longitude: coordinate(result.longitude),
      }
    }
    case IP_INFO_API.IPSB:
    default: {
      const result = await getIPFromIpsbAPI(ip, signal)

      return {
        ip: result.ip,
        country: result.country ?? '',
        region: result.region ?? '',
        city: result.city ?? '',
        asn: result.asn?.toString() ?? '',
        asnOrganization: result.asn_organization,
        organization: result.organization ?? result.asn_organization ?? '',
        latitude: coordinate(result.latitude),
        longitude: coordinate(result.longitude),
      }
    }
  }
}

// Backward-compatible facade for connection table callers. The heavy MMDB
// implementation remains behind connectionGeoip's dynamic import.
export const getGeoIPInfoSync = getConnectionGeoIPInfoSync
