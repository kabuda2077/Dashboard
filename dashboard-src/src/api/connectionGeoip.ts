import type { IPInfo } from '@/api/geoip'
import { shallowRef } from 'vue'

type GeoIPLookup = (ip: string) => IPInfo

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

// Keep the Buffer polyfill, MMDB parser and databases out of the initial bundle.
// The first visible GeoIP cell starts the async chunk/database load; subsequent
// reads are synchronous and reactive through connectionGeoipDatabase's cache.
const lookup = shallowRef<GeoIPLookup>()
let loadPromise: Promise<void> | undefined

const loadLookup = () => {
  if (lookup.value || loadPromise) return

  const currentLoad = import('./connectionGeoipDatabase')
    .then((module) => {
      lookup.value = module.getConnectionGeoIPInfoSync
    })
    .catch(() => {
      // A transient chunk/storage failure may be retried by a later render.
      if (loadPromise === currentLoad) loadPromise = undefined
    })

  loadPromise = currentLoad
}

export const getConnectionGeoIPInfoSync = (ip: string): IPInfo => {
  if (!lookup.value) {
    loadLookup()
    return EMPTY_GEOIP_INFO
  }

  return lookup.value(ip)
}
