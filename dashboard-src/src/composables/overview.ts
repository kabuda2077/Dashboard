import { ref } from 'vue'

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
