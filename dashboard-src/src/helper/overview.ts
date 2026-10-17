import { getIPFromCloudflareAPI, getIPFromIpipnetAPI, getIPInfo } from '@/api/geoip'
import {
  getBilibiliLatencyAPI,
  getCloudflareLatencyAPI,
  getGithubLatencyAPI,
  getGoogleLatencyAPI,
  getTaobaoLatencyAPI,
  getTelegramLatencyAPI,
  getTencentLatencyAPI,
  getYouTubeLatencyAPI,
  LATENCY_URLS,
} from '@/api/latency'
import { IP_INFO_API } from '@/constant'
import { backendSessionGeneration, backendSessionReady } from '@/helper/backendSession'
import { i18n } from '@/i18n'
import { IPInfoAPI } from '@/store/settings'
import * as ipaddr from 'ipaddr.js'
import { ref, watch, type Ref } from 'vue'

export type IPDetails = {
  location: string
  organization: string
  asn: string
}
export type IPObservation = {
  ip: string[]
  ipWithPrivacy: string[]
  details: IPDetails
  detailsWithPrivacy: IPDetails
  state: 'idle' | 'loading' | 'ready' | 'failed'
  locationState: 'idle' | 'loading' | 'ready' | 'partial'
}
const emptyDetails = (): IPDetails => ({ location: '', organization: '', asn: '' })
const emptyObservation = (): IPObservation => ({
  ip: [],
  ipWithPrivacy: [],
  details: emptyDetails(),
  detailsWithPrivacy: emptyDetails(),
  state: 'idle',
  locationState: 'idle',
})
export const ipForChina = ref(emptyObservation())
export const ipForGlobal = ref(emptyObservation())
export const isCheckingIPs = ref(false)
let ipQueryId = 0
let ipController: AbortController | undefined
let ipSource = IPInfoAPI.value
const ipMessage = (
  key: 'getting' | 'networkIPQueryFailed',
  state: IPObservation['state'],
): IPObservation => ({
  ip: [i18n.global.t(key), ''],
  ipWithPrivacy: [i18n.global.t(key), ''],
  details: emptyDetails(),
  detailsWithPrivacy: emptyDetails(),
  state,
  locationState: 'idle',
})
// Services may naturally return either address format; mask without forcing a protocol.
const maskIP = (ip: string) => {
  if (!ipaddr.isValid(ip)) throw new Error('Invalid IP response')
  const address = ipaddr.parse(ip)
  return address.kind() === 'ipv6'
    ? address.toNormalizedString().split(':').slice(0, 4).join(':') + ':****:****:****:****'
    : address.toString().split('.').slice(0, 2).join('.') + '.***.***'
}
const maskLocation = (country: string) => `${country.trim() || '**'} ** ** **`
const maskDetail = (value: string) =>
  value
    .trim()
    .split(/\s+/)
    .map((field, index) => (index === 0 ? field : '**'))
    .join(' ')
const formatASN = (asn: string) => {
  const value = asn.trim()
  if (!value) return ''
  return /^AS/i.test(value) ? `AS${value.slice(2)}` : `AS${value}`
}

export const getIPs = async () => {
  if (isCheckingIPs.value && ipSource === IPInfoAPI.value) return
  ipController?.abort()
  const controller = new AbortController()
  ipController = controller
  ipSource = IPInfoAPI.value
  const source = ipSource as IP_INFO_API
  const request = ++ipQueryId
  const isCurrent = () => request === ipQueryId && !controller.signal.aborted
  isCheckingIPs.value = true
  ipForChina.value = ipMessage('getting', 'loading')
  ipForGlobal.value = ipMessage('getting', 'loading')
  try {
    await Promise.all([
      (async () => {
        try {
          const observed = await getIPFromCloudflareAPI(controller.signal)
          if (!isCurrent()) return
          const masked = maskIP(observed.ip)
          const publish = (
            location: string,
            country: string,
            organization: string,
            asn: string,
            locationState: IPObservation['locationState'],
          ) => {
            const details = {
              location,
              organization,
              asn: formatASN(asn),
            }
            ipForGlobal.value = {
              ipWithPrivacy: [location, observed.ip],
              ip: [maskLocation(country), masked],
              detailsWithPrivacy: details,
              details: {
                location: maskLocation(country),
                organization: maskDetail(organization),
                asn: maskDetail(details.asn),
              },
              state: 'ready',
              locationState,
            }
          }
          // Enrichment only adds location; it never replaces the observed address.
          publish(observed.loc, observed.loc, '', '', 'loading')
          try {
            const info = await getIPInfo(observed.ip, source, controller.signal)
            if (!isCurrent()) return
            const parts = [info.country || observed.loc, info.region, info.city]
              .filter((part): part is string => typeof part === 'string' && part.trim().length > 0)
              .map((part) => part.trim())
            publish(
              [...new Set(parts)].join(' '),
              info.country || observed.loc,
              info.organization,
              [formatASN(info.asn), info.asn.trim() ? info.asnOrganization?.trim() : '']
                .filter(Boolean)
                .join(' '),
              info.organization && info.asn && parts.length ? 'ready' : 'partial',
            )
          } catch {
            if (isCurrent()) ipForGlobal.value.locationState = 'partial'
          }
        } catch {
          if (isCurrent()) ipForGlobal.value = ipMessage('networkIPQueryFailed', 'failed')
        }
      })(),
      (async () => {
        try {
          const res = await getIPFromIpipnetAPI(controller.signal)
          if (!isCurrent()) return
          if (
            !res.data ||
            !Array.isArray(res.data.location) ||
            res.data.location.some((part) => typeof part !== 'string')
          )
            throw new Error('Invalid IP response')
          const masked = maskIP(res.data.ip)
          const parts = res.data.location.map((part) => part.trim()).filter(Boolean)
          ipForChina.value = {
            ipWithPrivacy: [parts.join(' '), res.data.ip],
            details: emptyDetails(),
            detailsWithPrivacy: emptyDetails(),
            ip: [maskLocation(res.data.location[0] ?? ''), masked],
            state: 'ready',
            locationState: parts.length > 1 ? 'ready' : 'partial',
          }
        } catch {
          if (isCurrent()) ipForChina.value = ipMessage('networkIPQueryFailed', 'failed')
        }
      })(),
    ])
  } finally {
    if (isCurrent()) {
      isCheckingIPs.value = false
      ipController = undefined
    }
  }
}

// Shared ownership keeps source changes and in-flight results independent of page lifetime.
watch(
  IPInfoAPI,
  () => {
    if ([ipForChina, ipForGlobal].some((item) => item.value.state !== 'idle')) void getIPs()
  },
  { flush: 'sync' },
)

export const LATENCY_ROUNDS = 5
export const latencyTargets: {
  id: string
  name: string
  region: 'domestic' | 'international'
  url: string
  ref: Ref<number[]>
  api: (signal?: AbortSignal) => Promise<number>
}[] = [
  {
    id: 'bilibili',
    name: 'Bilibili',
    region: 'domestic',
    url: LATENCY_URLS.bilibili,
    ref: ref([]),
    api: getBilibiliLatencyAPI,
  },
  {
    id: 'tencent',
    name: 'Tencent',
    region: 'domestic',
    url: LATENCY_URLS.tencent,
    ref: ref([]),
    api: getTencentLatencyAPI,
  },
  {
    id: 'taobao',
    name: 'Taobao',
    region: 'domestic',
    url: LATENCY_URLS.taobao,
    ref: ref([]),
    api: getTaobaoLatencyAPI,
  },
  {
    id: 'github',
    name: 'GitHub',
    region: 'international',
    url: LATENCY_URLS.github,
    ref: ref([]),
    api: getGithubLatencyAPI,
  },
  {
    id: 'cloudflare',
    name: 'Cloudflare',
    region: 'international',
    url: LATENCY_URLS.cloudflare,
    ref: ref([]),
    api: getCloudflareLatencyAPI,
  },
  {
    id: 'google',
    name: 'Google',
    region: 'international',
    url: LATENCY_URLS.google,
    ref: ref([]),
    api: getGoogleLatencyAPI,
  },
  {
    id: 'telegram',
    name: 'Telegram',
    region: 'international',
    url: LATENCY_URLS.telegram,
    ref: ref([]),
    api: getTelegramLatencyAPI,
  },
  {
    id: 'youtube',
    name: 'YouTube',
    region: 'international',
    url: LATENCY_URLS.youtube,
    ref: ref([]),
    api: getYouTubeLatencyAPI,
  },
]
export const isTestingLatency = ref(false)
let latencyQueryId = 0
let latencyController: AbortController | undefined
export const getLatency = async () => {
  if (isTestingLatency.value) return
  const request = ++latencyQueryId
  const controller = new AbortController()
  latencyController = controller
  const isCurrent = () => request === latencyQueryId && !controller.signal.aborted
  isTestingLatency.value = true
  latencyTargets.forEach((target) => {
    target.ref.value = []
  })
  try {
    await Promise.all(
      latencyTargets.map(async (target) => {
        for (let i = 0; i < LATENCY_ROUNDS; i++) {
          if (!isCurrent()) return
          let result = 0
          try {
            result = await target.api(controller.signal)
          } catch {
            /* A failed round is represented by zero. */
          }
          if (!isCurrent()) return
          target.ref.value = [...target.ref.value, Math.round(result)]
        }
      }),
    )
  } finally {
    if (isCurrent()) {
      isTestingLatency.value = false
      latencyController = undefined
    }
  }
}

export const resetNetworkChecks = () => {
  ipQueryId++
  latencyQueryId++
  ipController?.abort()
  latencyController?.abort()
  ipController = undefined
  latencyController = undefined
  isCheckingIPs.value = false
  isTestingLatency.value = false
  ipForChina.value = emptyObservation()
  ipForGlobal.value = emptyObservation()
  latencyTargets.forEach((target) => {
    target.ref.value = []
  })
}

// Also allow standalone browser preview checks while idle; transitions still invalidate them.
watch([backendSessionGeneration, backendSessionReady], resetNetworkChecks, { flush: 'sync' })
