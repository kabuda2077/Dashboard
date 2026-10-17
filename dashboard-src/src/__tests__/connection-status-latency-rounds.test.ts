import ConnectionStatus from '@/components/overview/ConnectionStatus.vue'
import { getLatency, isTestingLatency, LATENCY_ROUNDS, latencyTargets } from '@/helper/overview'
import en from '@/i18n/en'
import { autoConnectionCheck } from '@/store/settings'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { createApp, nextTick, type App } from 'vue'
import { createI18n } from 'vue-i18n'

const fixtures = vi.hoisted(() => ({
  bilibili: vi.fn(),
  tencent: vi.fn(),
  taobao: vi.fn(),
  github: vi.fn(),
  cloudflare: vi.fn(),
  google: vi.fn(),
  telegram: vi.fn(),
  youtube: vi.fn(),
}))
vi.mock('@/api/latency', () => ({
  LATENCY_URLS: Object.fromEntries(
    Object.keys(fixtures).map((id) => [id, `https://${id}.test/favicon.ico`]),
  ),
  getBilibiliLatencyAPI: fixtures.bilibili,
  getTencentLatencyAPI: fixtures.tencent,
  getTaobaoLatencyAPI: fixtures.taobao,
  getGithubLatencyAPI: fixtures.github,
  getCloudflareLatencyAPI: fixtures.cloudflare,
  getGoogleLatencyAPI: fixtures.google,
  getTelegramLatencyAPI: fixtures.telegram,
  getYouTubeLatencyAPI: fixtures.youtube,
}))
vi.mock('@/helper/backendSession', async () => {
  const { ref } = await import('vue')
  return {
    backendSessionGeneration: ref(1),
    backendSessionReady: ref(true),
    captureBackendSession: () => ({ isCurrent: () => true }),
  }
})

let app: App | undefined
let root: HTMLDivElement
const pending = new Map<string, Array<(value: number) => void>>()
const mount = () => {
  root = document.createElement('div')
  document.body.append(root)
  app = createApp(ConnectionStatus)
  app.use(createI18n({ legacy: false, locale: 'en', messages: { en } }))
  app.mount(root)
}
beforeEach(() => {
  autoConnectionCheck.value = false
  for (const target of latencyTargets) {
    target.ref.value = []
    pending.set(target.id, [])
    const mock = fixtures[target.id as keyof typeof fixtures]
    mock
      .mockReset()
      .mockImplementation(
        () => new Promise<number>((resolve) => pending.get(target.id)!.push(resolve)),
      )
  }
  mount()
})
afterEach(() => {
  app?.unmount()
  app = undefined
  root.remove()
})
const finishRound = async (value: number) => {
  for (const target of latencyTargets) pending.get(target.id)!.shift()!(value)
  await nextTick()
  await nextTick()
}

it('shares one eight-target five-round task across remounts and keeps samples visible as dots', async () => {
  root.querySelector('button')!.click()
  expect(latencyTargets).toHaveLength(8)
  expect(latencyTargets.map(({ id }) => id)).toEqual([
    'bilibili',
    'tencent',
    'taobao',
    'github',
    'cloudflare',
    'google',
    'telegram',
    'youtube',
  ])
  expect(LATENCY_ROUNDS).toBe(5)
  expect(root.querySelectorAll('[data-network-target]')).toHaveLength(8)
  expect(root.querySelectorAll('[data-network-target] [role=img] span')).toHaveLength(40)
  app!.unmount()
  root.remove()
  autoConnectionCheck.value = true
  mount()
  expect(root.querySelector('button')!.disabled).toBe(true)
  await getLatency()
  for (const mock of Object.values(fixtures)) expect(mock).toHaveBeenCalledTimes(1)
  for (let round = 0; round < LATENCY_ROUNDS; round++) await finishRound(42)
  await vi.waitFor(() => expect(isTestingLatency.value).toBe(false))
  for (const target of latencyTargets) {
    expect(target.ref.value).toEqual(Array(5).fill(42))
    expect(root.querySelector(`[data-network-target=${target.id}]`)?.textContent).toContain('42ms')
  }
  for (const mock of Object.values(fixtures)) expect(mock).toHaveBeenCalledTimes(5)
})
it('shows failed and pending samples separately rather than dropping them from the chart', async () => {
  fixtures.bilibili.mockRejectedValue(new Error('offline'))
  root.querySelector('button')!.click()
  await vi.waitFor(() => expect(latencyTargets[0].ref.value).toHaveLength(5))
  const failedRow = root.querySelector('[data-network-target=bilibili]')!
  expect(failedRow.querySelectorAll('.bg-error')).toHaveLength(5)
  expect(failedRow.textContent).toContain('--')
  expect(
    root.querySelector('[data-network-target=github] [role=img]')?.getAttribute('aria-label'),
  ).toContain('0/0')
  for (let round = 0; round < LATENCY_ROUNDS; round++) {
    for (const target of latencyTargets.filter((target) => target.id !== 'bilibili'))
      pending.get(target.id)!.shift()!(25)
    await nextTick()
    await nextTick()
  }
  await vi.waitFor(() => expect(isTestingLatency.value).toBe(false))
})
it('resets existing samples on another manual test without launching duplicates', async () => {
  for (const target of latencyTargets) target.ref.value = [10, 20]
  root.querySelector('button')!.click()
  await nextTick()
  expect(latencyTargets.every((target) => target.ref.value.length === 0)).toBe(true)
  root.querySelector('button')!.click()
  for (const mock of Object.values(fixtures)) expect(mock).toHaveBeenCalledTimes(1)
  for (const value of [10, 0, 20, 40, 1000]) await finishRound(value)
  await vi.waitFor(() => expect(isTestingLatency.value).toBe(false))
  // The outlier affects the mean, not the displayed successful-sample median.
  for (const target of latencyTargets) {
    const row = root.querySelector(`[data-network-target=${target.id}]`)!
    expect(row.textContent).toContain('30ms')
    expect(row.querySelector('[role=img]')?.getAttribute('aria-label')).toContain('4/5')
  }
})
