import TimeSeriesChart from '@/components/charts/TimeSeriesChart.vue'
import MiniSparkline from '@/components/overview/MiniSparkline.vue'
import { useEChart } from '@/composables/useEChart'
import { describe, expect, it, vi } from 'vitest'
import { computed, createApp, defineComponent, h, KeepAlive, nextTick, ref } from 'vue'

const mockCharts = vi.hoisted(() => {
  const instances: Array<{
    setOption: ReturnType<typeof vi.fn>
    resize: ReturnType<typeof vi.fn>
    dispose: ReturnType<typeof vi.fn>
    clear: ReturnType<typeof vi.fn>
  }> = []
  return { instances }
})

vi.mock('echarts/core', () => ({
  use: vi.fn(),
  graphic: {
    LinearGradient: class {
      constructor(...args: unknown[]) {
        void args
      }
    },
  },
  init: vi.fn(() => {
    const chart = {
      setOption: vi.fn(),
      resize: vi.fn(),
      dispose: vi.fn(),
      clear: vi.fn(),
      dispatchAction: vi.fn(),
    }
    mockCharts.instances.push(chart)
    return chart
  }),
}))

// Hold the observed element size under test control; the component and all its watchers remain real.
const size = { width: ref(0), height: ref(0) }
vi.mock('@vueuse/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@vueuse/core')>()),
  useElementSize: () => size,
}))

const flush = async () => {
  await nextTick()
  await nextTick()
}

async function scenario(kind: 'sparkline' | 'time-series') {
  mockCharts.instances.length = 0
  size.width.value = 0
  const active = ref(true)
  const data = ref<[number, number][]>([[1000, 1]])
  const chartComponent = kind === 'sparkline' ? MiniSparkline : TimeSeriesChart
  const host = document.createElement('div')
  document.body.appendChild(host)
  const app = createApp(
    defineComponent({
      setup: () => () =>
        h(KeepAlive, null, [
          active.value
            ? h(
                chartComponent,
                kind === 'sparkline'
                  ? { data: data.value }
                  : {
                      data: [{ name: 'speed', data: data.value }],
                      labelFormatter: String,
                      tooltipFormatter: String,
                    },
              )
            : h('div', 'another route'),
        ]),
    }),
  )
  app.mount(host)
  await flush()
  const chart = mockCharts.instances[0]!
  const counts = () => [chart.setOption.mock.calls.length, chart.resize.mock.calls.length]
  const initial = counts()
  data.value = [[2000, 2]]
  await flush()
  const foreground = counts()
  active.value = false
  await flush()
  data.value = [[3000, 3]]
  await flush()
  size.width.value++
  await flush()
  await new Promise((resolve) => setTimeout(resolve, 130))
  const inactive = counts()
  active.value = true
  await flush()
  const resumed = counts()
  size.width.value++
  await flush()
  await new Promise((resolve) => setTimeout(resolve, 130))
  const resized = counts()
  return { chart, host, app, data, active, initial, foreground, inactive, resumed, resized, counts }
}

describe('mounted Overview chart lifecycle counts (mock ECharts)', () => {
  it('direct reactive source and element size while cached', async () => {
    mockCharts.instances.length = 0
    size.width.value = 0
    const active = ref(true)
    const value = ref(1)
    const host = document.createElement('div')
    document.body.appendChild(host)
    const Chart = defineComponent({
      setup() {
        const chartRef = ref<HTMLElement>()
        useEChart(
          chartRef,
          computed(() => ({ series: [{ data: [value.value] }] })),
        )
        return () => h('div', { ref: chartRef })
      },
    })
    const app = createApp(
      defineComponent({
        setup: () => () => h(KeepAlive, null, [active.value ? h(Chart) : h('div', 'elsewhere')]),
      }),
    )
    app.mount(host)
    await flush()
    const chart = mockCharts.instances[0]!
    const counts = () => [chart.setOption.mock.calls.length, chart.resize.mock.calls.length]
    const initial = counts()
    active.value = false
    await flush()
    value.value++
    size.width.value++
    await flush()
    await new Promise((resolve) => setTimeout(resolve, 130))
    const inactive = counts()
    active.value = true
    await flush()
    const resumed = counts()
    console.info('direct counts', { initial, inactive, resumed })
    expect(initial).toEqual([1, 0])
    expect(inactive).toEqual(initial)
    expect(resumed).toEqual([2, 1])
    expect(chart.setOption.mock.lastCall?.[0].series[0].data).toEqual([2])
    app.unmount()
    host.remove()
  })
  it.each(['sparkline', 'time-series'] as const)('%s activation and deactivation', async (kind) => {
    const run = await scenario(kind)
    try {
      console.info(kind + ' counts', {
        initial: run.initial,
        foreground: run.foreground,
        inactive: run.inactive,
        resumed: run.resumed,
        resized: run.resized,
      })
      expect(run.initial[0]).toBeGreaterThanOrEqual(1)
      expect(run.foreground).toEqual([run.initial[0] + 1, run.initial[1]])
      expect(run.inactive).toEqual(run.foreground)
      expect(run.resumed).toEqual([run.inactive[0] + 1, run.inactive[1] + 1])
      expect(run.resized).toEqual([run.resumed[0], run.resumed[1] + 1])
      if (kind === 'time-series') {
        const button = run.host.querySelector('button')!
        button.click() // user pause, independent of route deactivation
        await flush()
        const paused = run.counts()
        run.active.value = false
        await flush()
        run.data.value = [[4000, 4]]
        await flush()
        run.active.value = true
        await flush()
        expect(run.counts()).toEqual([paused[0], paused[1] + 1])
        button.click() // resume with latest data
        await flush()
        expect(run.counts()[0]).toBe(paused[0] + 1)
      }
    } finally {
      run.app.unmount()
      run.host.remove()
    }
  })
})
