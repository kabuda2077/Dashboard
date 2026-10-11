import { getIPFromIpipnetAPI, getIPInfo } from '@/api/geoip'
import {
  getBaiduLatencyAPI,
  getCloudflareLatencyAPI,
  getGithubLatencyAPI,
  getYouTubeLatencyAPI,
} from '@/api/latency'
import { i18n } from '@/i18n'
import { IPInfoAPI } from '@/store/settings'
import * as ipaddr from 'ipaddr.js'
import { ref, watch } from 'vue'

type IPInfo = {
  ip: string[]
  ipWithPrivacy: string[]
}

export const ipForChina = ref<IPInfo>({
  ip: [],
  ipWithPrivacy: [],
})
export const ipForGlobal = ref<IPInfo>({
  ip: [],
  ipWithPrivacy: [],
})

// Each target keeps multiple latency samples in milliseconds. A value of 0 means that round failed.
export const baiduLatency = ref<number[]>([])
export const githubLatency = ref<number[]>([])
export const youtubeLatency = ref<number[]>([])
export const cloudflareLatency = ref<number[]>([])

export const isCheckingIPs = ref(false)
let ipQueryId = 0
let ipSource = IPInfoAPI.value
const ipMessage = (key: 'getting' | 'testFailed'): IPInfo => ({
  ip: [i18n.global.t(key), ''],
  ipWithPrivacy: [i18n.global.t(key), ''],
})
const maskIP = (ip: string) => {
  if (!ipaddr.isValid(ip)) throw new Error('Invalid IP response')
  const address = ipaddr.parse(ip)
  return address.kind() === 'ipv6'
    ? address.toNormalizedString().split(':').slice(0, 2).join(':') + ':****:****'
    : '***.***.***.***'
}

export const getIPs = async () => {
  if (isCheckingIPs.value && ipSource === IPInfoAPI.value) return
  ipSource = IPInfoAPI.value
  const request = ++ipQueryId
  isCheckingIPs.value = true
  ipForChina.value = ipMessage('getting')
  ipForGlobal.value = ipMessage('getting')
  try {
    await Promise.all([
      getIPInfo()
        .then((res) => {
          if (request !== ipQueryId) return
          const masked = maskIP(res.ip)
          ipForGlobal.value = {
            ipWithPrivacy: [`${res.country} ${res.organization}`, res.ip],
            ip: [`${res.country} ${res.organization}`, masked],
          }
        })
        .catch(() => {
          if (request === ipQueryId) ipForGlobal.value = ipMessage('testFailed')
        }),
      getIPFromIpipnetAPI()
        .then((res) => {
          if (request !== ipQueryId) return
          if (!res.data || !Array.isArray(res.data.location)) throw new Error('Invalid IP response')
          const masked = maskIP(res.data.ip)
          ipForChina.value = {
            ipWithPrivacy: [res.data.location.join(' '), res.data.ip],
            ip: [`${res.data.location[0] ?? '**'} ** ** **`, masked],
          }
        })
        .catch(() => {
          if (request === ipQueryId) ipForChina.value = ipMessage('testFailed')
        }),
    ])
  } finally {
    if (request === ipQueryId) isCheckingIPs.value = false
  }
}

// Module ownership keeps source changes and in-flight results independent of page lifetime.
watch(
  IPInfoAPI,
  () => {
    if ([ipForChina, ipForGlobal].some((item) => item.value.ip.length !== 0)) void getIPs()
  },
  { flush: 'sync' },
)

export const LATENCY_ROUNDS = 10
export const latencyTargets = [
  { name: 'Baidu', ref: baiduLatency, api: getBaiduLatencyAPI },
  { name: 'Cloudflare', ref: cloudflareLatency, api: getCloudflareLatencyAPI },
  { name: 'GitHub', ref: githubLatency, api: getGithubLatencyAPI },
  { name: 'YouTube', ref: youtubeLatency, api: getYouTubeLatencyAPI },
]
export const isTestingLatency = ref(false)
export const getLatency = async () => {
  if (isTestingLatency.value) return
  isTestingLatency.value = true
  latencyTargets.forEach((target) => {
    target.ref.value = []
  })
  try {
    await Promise.all(
      latencyTargets.map(async (target) => {
        for (let i = 0; i < LATENCY_ROUNDS; i++) {
          let result = 0
          try {
            result = await target.api()
          } catch {
            /* A failed round is represented by zero. */
          }
          target.ref.value = [...target.ref.value, Math.round(result)]
        }
      }),
    )
  } finally {
    isTestingLatency.value = false
  }
}
