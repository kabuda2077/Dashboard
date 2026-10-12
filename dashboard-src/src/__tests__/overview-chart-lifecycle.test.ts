import TimeSeriesChart from '@/components/charts/TimeSeriesChart.vue'
import ConnectionsCharts from '@/components/overview/ConnectionsCharts.vue'
import MemoryCharts from '@/components/overview/MemoryCharts.vue'
import MiniSparkline from '@/components/overview/MiniSparkline.vue'
import SpeedCharts from '@/components/overview/SpeedCharts.vue'
import RuleHitCountChart from '@/components/overview/RuleHitCountChart.vue'
import TopologyCharts from '@/components/overview/TopologyCharts.vue'
import { isMiddleScreen } from '@/helper/utils'
import en from '@/i18n/en'
import zh from '@/i18n/zh'
import { createI18n } from 'vue-i18n'
import { useEChart } from '@/composables/use-echart'
import { describe, expect, it, vi } from 'vitest'
import { computed, createApp, defineComponent, h, nextTick, ref } from 'vue'

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
      on: vi.fn(),
      off: vi.fn(),
    }
    mockCharts.instances.push(chart)
    return chart
  }),
}))

// Hold the observed element size under test control; the component and all its watchers remain real.
const size = { width: ref(0), height: ref(0) }
const viewport = { width: ref(640), height: ref(1000) }
vi.mock('@vueuse/core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@vueuse/core')>()),
  useElementSize: () => size,
  useWindowSize: () => viewport,
}))

const flush = async () => {
  await nextTick()
  await nextTick()
}

describe('mounted Overview chart lifecycle (mock ECharts)', () => {
  it('keeps a full-screen topology upright in a narrow portrait desktop viewport', async () => {
    const wasNarrow = isMiddleScreen.value
    isMiddleScreen.value = true
    const host = document.createElement('div')
    document.body.appendChild(host)
    const app = createApp(TopologyCharts)
    const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
    app.use(i18n)
    try {
      app.mount(host)
      await flush()
      host.querySelector<HTMLButtonElement>(`button[aria-label="${en.topologyExpand}"]`)!.click()
      await flush()
      const overlay = document.body.querySelector<HTMLElement>('.fixed.inset-0.z-\\[9999\\]')
      expect(overlay).not.toBeNull()
      const surface = overlay!.querySelector<HTMLElement>('.overflow-hidden > .relative')!
      expect(surface.style.transform).toBe('')
      expect(surface.style.width).toBe('100%')
      expect(surface.style.height).toBe('100%')
      overlay!.querySelector<HTMLButtonElement>(`button[aria-label="${en.topologyCollapse}"]`)!.click()
      await flush()
      expect(document.body.querySelector('.fixed.inset-0.z-\\[9999\\]')).toBeNull()
      expect(host.querySelector('button[aria-label]')).not.toBeNull()
    } finally {
      app.unmount()
      host.remove()
      isMiddleScreen.value = wasNarrow
    }
  })

  it.each(['en', 'zh'] as const)('names each real chart toggle and exposes pause state (%s)', async (locale) => {
    const i18n = createI18n({ legacy: false, locale, messages: { en, zh } })
    const host = document.createElement('div')
    document.body.appendChild(host)
    const app = createApp({ render: () => h('div', [h(ConnectionsCharts), h(MemoryCharts), h(SpeedCharts)]) })
    app.use(i18n)
    try {
      app.mount(host)
      await flush()
      const buttons = [...host.querySelectorAll('button')]
      expect(buttons.map((button) => button.getAttribute('aria-label'))).toEqual(
        ['connections', 'memoryUsage', 'traffic'].map((key) => i18n.global.t(key)),
      )
      expect(host.querySelectorAll('.size-1\\.5')).toHaveLength(2)
      expect(mockCharts.instances.slice(-3).every((chart) => !('legend' in chart.setOption.mock.lastCall![0]))).toBe(true)
      for (const button of buttons) {
        button.focus()
        expect(document.activeElement).toBe(button)
        expect(button.getAttribute('aria-pressed')).toBe('false')
        button.click()
        await flush()
        expect(button.getAttribute('aria-pressed')).toBe('true')
        button.click()
        await flush()
        expect(button.getAttribute('aria-pressed')).toBe('false')
      }
    } finally {
      app.unmount()
      host.remove()
    }
  })

  it('names rule count controls and keeps pause state accessible', async () => {
    const host = document.createElement('div')
    document.body.append(host)
    const app = createApp({ render: () => h('div', [h(RuleHitCountChart, { type: 'hit' }), h(RuleHitCountChart, { type: 'miss' })]) })
    app.use(createI18n({ legacy: false, locale: 'en', messages: { en } }))
    try {
      app.mount(host)
      await flush()
      const buttons = [...host.querySelectorAll('button')]
      expect(buttons.map((button) => button.getAttribute('aria-label'))).toEqual([en.hitCount, en.missCount])
      buttons[0].click()
      await flush()
      expect(buttons[0].getAttribute('aria-pressed')).toBe('true')
      expect(buttons[1].getAttribute('aria-pressed')).toBe('false')
    } finally { app.unmount(); host.remove() }
  })

  it('pauses hidden or disconnected charts and cancels pending resize on unmount', async () => {
    vi.useFakeTimers()
    mockCharts.instances.length = 0
    size.width.value = 0
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    const value = ref(1)
    const cleanup = vi.fn()
    const host = document.createElement('div')
    document.body.appendChild(host)
    const Chart = defineComponent({
      setup() {
        const chartRef = ref<HTMLElement>()
        useEChart(
          chartRef,
          computed(() => ({ series: [{ data: [value.value] }] })),
          { onInit: () => cleanup },
        )
        return () => h('div', { ref: chartRef })
      },
    })
    const app = createApp(Chart)
    let unmounted = false
    try {
      app.mount(host)
      await flush()
      const chart = mockCharts.instances[0]!
      expect(chart.setOption).toHaveBeenCalledOnce()

      visibility.mockReturnValue('hidden')
      document.dispatchEvent(new Event('visibilitychange'))
      value.value = 2
      size.width.value++
      await flush()
      await vi.advanceTimersByTimeAsync(130)
      expect(chart.setOption).toHaveBeenCalledOnce()
      expect(chart.resize).not.toHaveBeenCalled()

      visibility.mockReturnValue('visible')
      document.dispatchEvent(new Event('visibilitychange'))
      expect(chart.setOption.mock.lastCall?.[0].series[0].data).toEqual([2])
      expect(chart.resize).toHaveBeenCalledOnce()

      host.remove()
      value.value = 3
      size.width.value++
      await flush()
      await vi.advanceTimersByTimeAsync(130)
      document.dispatchEvent(new Event('visibilitychange'))
      expect(chart.setOption).toHaveBeenCalledTimes(2)
      expect(chart.resize).toHaveBeenCalledOnce()

      document.body.appendChild(host)
      size.width.value++
      await flush()
      app.unmount()
      unmounted = true
      await vi.advanceTimersByTimeAsync(130)
      document.dispatchEvent(new Event('visibilitychange'))
      value.value = 4
      await flush()
      expect(chart.setOption).toHaveBeenCalledTimes(2)
      expect(chart.resize).toHaveBeenCalledOnce()
      expect(chart.dispose).toHaveBeenCalledOnce()
      expect(cleanup).toHaveBeenCalledOnce()
    } finally {
      if (!unmounted) app.unmount()
      host.remove()
      visibility.mockRestore()
      vi.useRealTimers()
    }
  })

  it.each(['sparkline', 'time-series'] as const)(
    '%s disposes on leaving and remounts with current data',
    async (kind) => {
      vi.useFakeTimers()
      mockCharts.instances.length = 0
      size.width.value = 0
      const active = ref(true)
      const data = ref<[number, number][]>([[1000, 1]])
      const chartComponent = kind === 'sparkline' ? MiniSparkline : TimeSeriesChart
      const host = document.createElement('div')
      document.body.appendChild(host)
      const app = createApp({
        setup: () => () =>
          active.value
            ? h(
                chartComponent,
                kind === 'sparkline'
                  ? { data: data.value }
                  : {
                      title: 'Traffic',
                      data: [{ name: 'speed', data: data.value }],
                      labelFormatter: String,
                      tooltipFormatter: String,
                    },
              )
            : h('div', 'another route'),
      })
      try {
        app.mount(host)
        await flush()
        const first = mockCharts.instances[0]!
        const initialRenders = first.setOption.mock.calls.length
        data.value = [[2000, 2]]
        await flush()
        expect(first.setOption).toHaveBeenCalledTimes(initialRenders + 1)
        size.width.value++
        await flush()
        await vi.advanceTimersByTimeAsync(130)
        expect(first.resize).toHaveBeenCalledOnce()

        active.value = false
        await flush()
        expect(first.dispose).toHaveBeenCalledOnce()
        const rendersAtUnmount = first.setOption.mock.calls.length
        data.value = [[3000, 3]]
        size.width.value++
        await flush()
        await vi.advanceTimersByTimeAsync(130)
        expect(first.setOption).toHaveBeenCalledTimes(rendersAtUnmount)
        expect(first.resize).toHaveBeenCalledOnce()
        expect(mockCharts.instances).toHaveLength(1)

        active.value = true
        await flush()
        expect(mockCharts.instances).toHaveLength(2)
        const second = mockCharts.instances[1]!
        expect(second.setOption.mock.lastCall?.[0].series[0].data).toEqual([[3000, 3]])
        size.width.value++
        await flush()
        await vi.advanceTimersByTimeAsync(130)
        expect(second.resize).toHaveBeenCalledOnce()
        if (kind === 'time-series') {
          const button = host.querySelector('button')!
          button.click()
          await flush()
          const pausedRenders = second.setOption.mock.calls.length
          data.value = [[4000, 4]]
          await flush()
          expect(second.setOption).toHaveBeenCalledTimes(pausedRenders)
          button.click()
          await flush()
          expect(second.setOption).toHaveBeenCalledTimes(pausedRenders + 1)
          expect(second.setOption.mock.lastCall?.[0].series[0].data).toEqual([[4000, 4]])
        }
      } finally {
        app.unmount()
        host.remove()
        vi.useRealTimers()
      }
      expect(mockCharts.instances).toHaveLength(2)
      for (const chart of mockCharts.instances) expect(chart.dispose).toHaveBeenCalledOnce()
    },
  )
})
