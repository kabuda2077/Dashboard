export const LATENCY_URLS = {
  bilibili: 'https://www.bilibili.com/robots.txt',
  tencent: 'https://www.qq.com/robots.txt',
  taobao: 'https://www.taobao.com/robots.txt',
  github: 'https://github.com/robots.txt',
  cloudflare: 'https://cp.cloudflare.com/generate_204',
  google: 'https://www.google.com/generate_204',
  telegram: 'https://telegram.org/robots.txt',
  youtube: 'https://www.youtube.com/generate_204',
} as const

// Time to response headers through the WebView's current network path, not ICMP
// or node URL-test latency. Opaque responses cannot establish HTTP status/unlock.
export const getLatencyFromUrlAPI = async (url: string, signal?: AbortSignal) => {
  if (signal?.aborted) return 0
  const controller = new AbortController()
  const abort = () => controller.abort()
  const timeout = setTimeout(abort, 5000)
  signal?.addEventListener('abort', abort, { once: true })
  const started = performance.now()
  try {
    const resource = new URL(url)
    resource.searchParams.set('_dashboard_latency', crypto.randomUUID())
    const response = await fetch(resource.href, {
      method: 'GET',
      mode: 'no-cors',
      credentials: 'omit',
      cache: 'no-store',
      redirect: 'follow',
      referrerPolicy: 'no-referrer',
      signal: controller.signal,
    })
    const elapsed = Math.max(1, performance.now() - started)
    // Stop body transfer even when an endpoint changes into a larger resource.
    // Opaque bodies are inaccessible; abort in finally also closes those.
    void response.body?.cancel().catch(() => {})
    return controller.signal.aborted ? 0 : elapsed
  } catch {
    return 0
  } finally {
    clearTimeout(timeout)
    signal?.removeEventListener('abort', abort)
    controller.abort()
  }
}

export const getBilibiliLatencyAPI = (signal?: AbortSignal) =>
  getLatencyFromUrlAPI(LATENCY_URLS.bilibili, signal)
export const getTencentLatencyAPI = (signal?: AbortSignal) =>
  getLatencyFromUrlAPI(LATENCY_URLS.tencent, signal)
export const getTaobaoLatencyAPI = (signal?: AbortSignal) =>
  getLatencyFromUrlAPI(LATENCY_URLS.taobao, signal)
export const getGithubLatencyAPI = (signal?: AbortSignal) =>
  getLatencyFromUrlAPI(LATENCY_URLS.github, signal)
export const getCloudflareLatencyAPI = (signal?: AbortSignal) =>
  getLatencyFromUrlAPI(LATENCY_URLS.cloudflare, signal)
export const getGoogleLatencyAPI = (signal?: AbortSignal) =>
  getLatencyFromUrlAPI(LATENCY_URLS.google, signal)
export const getTelegramLatencyAPI = (signal?: AbortSignal) =>
  getLatencyFromUrlAPI(LATENCY_URLS.telegram, signal)
export const getYouTubeLatencyAPI = (signal?: AbortSignal) =>
  getLatencyFromUrlAPI(LATENCY_URLS.youtube, signal)
