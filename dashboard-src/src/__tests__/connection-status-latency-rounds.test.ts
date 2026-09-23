import ConnectionStatus from '@/components/overview/ConnectionStatus.vue'
import {
  baiduLatency,
  cloudflareLatency,
  githubLatency,
  youtubeLatency,
} from '@/composables/overview'
import { autoConnectionCheck } from '@/store/settings'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, nextTick, type App } from 'vue'

const latencyAPIs = vi.hoisted(() => ({
  baidu: vi.fn<() => Promise<number>>(),
  cloudflare: vi.fn<() => Promise<number>>(),
  github: vi.fn<() => Promise<number>>(),
  youtube: vi.fn<() => Promise<number>>(),
}))

vi.mock('@/api/latency', () => ({
  getBaiduLatencyAPI: latencyAPIs.baidu,
  getCloudflareLatencyAPI: latencyAPIs.cloudflare,
  getGithubLatencyAPI: latencyAPIs.github,
  getYouTubeLatencyAPI: latencyAPIs.youtube,
}))

// Keep ConnectionStatus and LatencyChart real; only replace the ECharts-facing rendering boundary.
vi.mock('@/components/overview/MiniSparkline.vue', async () => {
  const { defineComponent, h } = await import('vue')

  return {
    default: defineComponent({
      name: 'MiniSparklineBoundary',
      props: { data: { type: Array, required: true } },
      setup(props) {
        return () => h('div', { 'data-chart-points': JSON.stringify(props.data) })
      },
    }),
  }
})

type Target = keyof typeof latencyAPIs

const targetState = {
  baidu: baiduLatency,
  cloudflare: cloudflareLatency,
  github: githubLatency,
  youtube: youtubeLatency,
}
const pending = {
  baidu: [] as Array<(value: number) => void>,
  cloudflare: [] as Array<(value: number) => void>,
  github: [] as Array<(value: number) => void>,
  youtube: [] as Array<(value: number) => void>,
}
const targets = Object.keys(latencyAPIs) as Target[]

let app: App | undefined
let host: HTMLDivElement | undefined

const rowText = (name: string) => {
  const row = Array.from(host!.querySelectorAll('.grid > div')).find((element) =>
    element.textContent?.includes(name),
  )
  expect(row, `latency row for ${name}`).toBeTruthy()
  return row!.textContent!.replace(/\s+/g, '')
}

const resolveRound = async (values: Record<Target, number[]>, round: number) => {
  for (const target of targets) {
    const resolve = pending[target].shift()
    expect(resolve, `pending ${target} request for round ${round + 1}`).toBeTypeOf('function')
    resolve!(values[target][round]!)
  }
  await nextTick()
}

const expectCallCount = async (count: number) => {
  await vi.waitFor(() => {
    for (const target of targets) expect(latencyAPIs[target]).toHaveBeenCalledTimes(count)
  })
}

describe('mounted ConnectionStatus latency rounds', () => {
  beforeEach(() => {
    autoConnectionCheck.value = false
    for (const target of targets) {
      targetState[target].value = []
      pending[target].length = 0
      latencyAPIs[target].mockReset()
      latencyAPIs[target].mockImplementation(
        () => new Promise<number>((resolve) => pending[target].push(resolve)),
      )
    }

    host = document.createElement('div')
    document.body.appendChild(host)
    app = createApp(ConnectionStatus)
    app.config.globalProperties.$t = (key: string) => key
    app.mount(host)
  })

  afterEach(() => {
    app?.unmount()
    host?.remove()
    app = undefined
    host = undefined
  })

  it('waits for all 10 samples before averaging each target, reports ranges, and resets safely', async () => {
    const firstRun = {
      baidu: [10, 20, 30, 40, 50, 60, 70, 80, 90, 100],
      cloudflare: [11, 12, 13, 14, 15, 16, 17, 18, 19, 20],
      github: [51, 52, 53, 54, 55, 56, 57, 58, 59, 60],
      youtube: [100, 101, 102, 103, 104, 105, 106, 107, 108, 109],
    }
    const button = host!.querySelector('button')!

    button.click()
    await nextTick()
    expect(button.disabled).toBe(true)
    await expectCallCount(1)

    for (let round = 0; round < 9; round++) {
      await resolveRound(firstRun, round)
      await expectCallCount(round + 2)
    }

    expect(targets.map((target) => latencyAPIs[target].mock.calls.length)).toEqual([10, 10, 10, 10])
    expect(targets.map((target) => targetState[target].value.length)).toEqual([9, 9, 9, 9])
    expect(rowText('Baidu')).toBe('Baidu--min10msmax90ms')
    expect(rowText('Cloudflare')).toBe('Cloudflare--min11msmax19ms')
    expect(rowText('GitHub')).toBe('GitHub--min51msmax59ms')
    expect(rowText('YouTube')).toBe('YouTube--min100msmax108ms')

    await resolveRound(firstRun, 9)
    await vi.waitFor(() => expect(button.disabled).toBe(false))

    expect(targets.map((target) => latencyAPIs[target].mock.calls.length)).toEqual([10, 10, 10, 10])
    expect(rowText('Baidu')).toBe('Baidu55msmin10msmax100ms')
    expect(rowText('Cloudflare')).toBe('Cloudflare16msmin11msmax20ms')
    expect(rowText('GitHub')).toBe('GitHub56msmin51msmax60ms')
    expect(rowText('YouTube')).toBe('YouTube105msmin100msmax109ms')

    button.click()
    await nextTick()
    expect(button.disabled).toBe(true)
    expect(targets.map((target) => targetState[target].value)).toEqual([[], [], [], []])
    await expectCallCount(11)

    button.click()
    await nextTick()
    expect(targets.map((target) => latencyAPIs[target].mock.calls.length)).toEqual([11, 11, 11, 11])

    const secondRun = {
      baidu: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
      cloudflare: [21, 22, 23, 24, 25, 26, 27, 28, 29, 30],
      github: [31, 32, 33, 34, 35, 36, 37, 38, 39, 40],
      youtube: [41, 42, 43, 44, 45, 46, 47, 48, 49, 50],
    }
    for (let round = 0; round < 10; round++) {
      await resolveRound(secondRun, round)
      if (round < 9) await expectCallCount(round + 12)
    }
    await vi.waitFor(() => expect(button.disabled).toBe(false))

    expect(targets.map((target) => latencyAPIs[target].mock.calls.length)).toEqual([20, 20, 20, 20])
    expect(targets.map((target) => targetState[target].value)).toEqual([
      secondRun.baidu,
      secondRun.cloudflare,
      secondRun.github,
      secondRun.youtube,
    ])
    expect(rowText('Baidu')).toBe('Baidu6msmin1msmax10ms')
  })
})
