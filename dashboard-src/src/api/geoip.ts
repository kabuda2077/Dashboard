import { getConnectionGeoIPInfoSync } from '@/api/connectionGeoip'
import { IP_INFO_API } from '@/constant'
import { IPInfoAPI } from '@/store/settings'
import * as ipaddr from 'ipaddr.js'

export interface IPInfo {
  ip: string
  country: string
  region: string
  city: string
  asn: string
  organization: string
  latitude: number | null
  longitude: number | null
}

const coordinate = (value: unknown) =>
  typeof value === 'number' && Number.isFinite(value) ? value : null

const ensureResponseOK = (response: Response, service: string) => {
  if (!response.ok) throw new Error(`${service} lookup failed: ${response.status}`)
}

// China public-IP service. Query-string cache busting can make ipip.net omit CORS headers.
export const getIPFromIpipnetAPI = async () => {
  const response = await fetch('https://myip.ipip.net/json', { cache: 'no-store' })
  ensureResponseOK(response, 'ipip.net')

  return (await response.json()) as {
    ret?: string
    data: {
      ip: string
      location: string[]
    }
  }
}

export const getIPFromIpsbAPI = async (ip = '') => {
  const response = await fetch('https://api.ip.sb/geoip' + (ip ? `/${ip}` : ''), {
    cache: 'no-store',
  })
  ensureResponseOK(response, IP_INFO_API.IPSB)

  return (await response.json()) as {
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

const getIPFromIPWhoisAPI = async (ip = '') => {
  const response = await fetch('https://ipwho.is' + (ip ? `/${ip}` : ''), {
    cache: 'no-store',
  })
  ensureResponseOK(response, IP_INFO_API.IPWHOIS)

  return (await response.json()) as
    | {
        ip: string
        success: true
        country?: string
        region?: string
        city?: string
        latitude?: number
        longitude?: number
        connection?: { asn?: number; org?: string }
      }
    | { ip?: string; success: false; message: string }
}

const getIPFromIPapiisAPI = async (ip = '') => {
  const response = await fetch('https://api.ipapi.is' + (ip ? `/?q=${ip}` : ''), {
    cache: 'no-store',
  })
  ensureResponseOK(response, IP_INFO_API.IPAPI)

  return (await response.json()) as
    | {
        ip: string
        company_name: string | null
        asn_num: number | null
        asn_org: string | null
        cc: string | null
        lat: number | null
        lon: number | null
      }
    | { error: string }
}

export const getIPInfo = async (
  ip = '',
  api: IP_INFO_API = IPInfoAPI.value as IP_INFO_API,
): Promise<IPInfo> => {
  switch (api) {
    case IP_INFO_API.IPAPI: {
      const result = await getIPFromIPapiisAPI(ip)
      if ('error' in result) throw new Error(`ipapi.is lookup failed: ${result.error}`)

      return {
        ip: result.ip,
        country: result.cc ?? '',
        region: '',
        city: '',
        asn: result.asn_num?.toString() ?? '',
        organization: result.asn_org ?? result.company_name ?? '',
        latitude: coordinate(result.lat),
        longitude: coordinate(result.lon),
      }
    }
    case IP_INFO_API.IPWHOIS: {
      const result = await getIPFromIPWhoisAPI(ip)
      if (!result.success) throw new Error(`IPWhois lookup failed: ${result.message}`)

      return {
        ip: result.ip,
        country: result.country ?? '',
        region: result.region ?? '',
        city: result.city ?? '',
        asn: result.connection?.asn?.toString() ?? '',
        organization: result.connection?.org ?? '',
        latitude: coordinate(result.latitude),
        longitude: coordinate(result.longitude),
      }
    }
    case IP_INFO_API.IPSB:
    default: {
      const result = await getIPFromIpsbAPI(ip)

      return {
        ip: result.ip,
        country: result.country ?? '',
        region: result.region ?? '',
        city: result.city ?? '',
        asn: result.asn?.toString() ?? '',
        organization: result.organization ?? result.asn_organization ?? '',
        latitude: coordinate(result.latitude),
        longitude: coordinate(result.longitude),
      }
    }
  }
}

export const getPublicIPInfo = async (api: IP_INFO_API): Promise<IPInfo> => {
  const info = await getIPInfo('', api)
  if (!ipaddr.isValid(info.ip)) throw new Error(`${api} returned an invalid public IP`)
  return info
}

// Backward-compatible facade for connection table callers. The heavy MMDB
// implementation remains behind connectionGeoip's dynamic import.
export const getGeoIPInfoSync = getConnectionGeoIPInfoSync
