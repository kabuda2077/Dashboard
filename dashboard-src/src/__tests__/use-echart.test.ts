import { useChartTheme, useEChart } from '@/composables/use-echart'
import type { EChartsCoreOption } from 'echarts/core'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, createApp, defineComponent, h, nextTick, ref } from 'vue'

const mocks = vi.hoisted(() => {
  const chart = {
    clear: vi.fn(),
    dispatchAction: vi.fn(),
    dispose: vi.fn(),
    resize: vi.fn(),
    setOption: vi.fn(),
  }

  return {
    chart,
    init: vi.fn(() => chart),
  }
})

vi.mock('echarts/core', () => ({
  init: mocks.init,
  use: vi.fn(),
}))

describe('useEChart', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders, resumes with current data, clears empty data, and disposes', async () => {
    const paused = ref(false)
    const isEmpty = ref(false)
    const value = ref(1)
    const host = document.createElement('div')
    document.body.appendChild(host)

    const app = createApp(
      defineComponent({
        setup() {
          const chartRef = ref<HTMLElement>()
          const options = computed<EChartsCoreOption>(() => ({ series: [{ data: [value.value] }] }))

          useEChart(chartRef, options, { paused, isEmpty })

          return () => h('div', { ref: chartRef })
        },
      }),
    )

    app.mount(host)
    await nextTick()
    await nextTick()

    expect(mocks.init).toHaveBeenCalledOnce()
    expect(mocks.chart.setOption).toHaveBeenCalled()

    paused.value = true
    value.value = 2
    await nextTick()
    const callsWhilePaused = mocks.chart.setOption.mock.calls.length

    paused.value = false
    await nextTick()
    expect(mocks.chart.setOption.mock.calls.length).toBeGreaterThan(callsWhilePaused)

    isEmpty.value = true
    await nextTick()
    expect(mocks.chart.clear).toHaveBeenCalled()

    app.unmount()
    expect(mocks.chart.dispose).toHaveBeenCalledOnce()
    host.remove()
  })

  it('mounts late rule data with connected theme colors and refreshes them after remount', async () => {
    // Chromium returns empty computed colors for detached elements.
    const { autoTheme, defaultTheme } = await import('@/store/settings')
    const wasAutoTheme = autoTheme.value
    const wasDefaultTheme = defaultTheme.value
    const wasColorScheme = document.documentElement.style.colorScheme
    autoTheme.value = false
    defaultTheme.value = 'light'
    document.documentElement.style.colorScheme = 'light'
    let themeColor = 'rgb(39, 39, 42)'
    const getStyle = globalThis.getComputedStyle
    const styleSpy = vi.spyOn(globalThis, 'getComputedStyle').mockImplementation(
      (element) =>
        new Proxy(getStyle(element), {
          get(target, property) {
            if (property === 'borderBottomColor' || property === 'borderTopColor') {
              return element.isConnected ? themeColor : ''
            }
            const value = Reflect.get(target, property, target)
            return typeof value === 'function' ? value.bind(target) : value
          },
        }),
    )
    const active = ref(true)
    const hasRules = ref(false)
    const value = ref(10)
    const Chart = defineComponent({
      setup() {
        const chartRef = ref<HTMLElement>()
        const { colors } = useChartTheme(chartRef)
        useEChart(
          chartRef,
          computed(() => ({
            color: colors.seriesPrimary,
            series: [{ data: [value.value], itemStyle: { color: colors.primary30 } }],
          })),
        )
        return () => h('div', [h('div', { ref: chartRef })])
      },
    })
    const Overview = defineComponent({
      setup: () => () => (hasRules.value ? h(Chart) : null),
    })
    const host = document.createElement('div')
    document.body.appendChild(host)
    const app = createApp({
      setup: () => () => (active.value ? h(Overview) : h('div', 'Rules')),
    })

    try {
      app.mount(host)
      active.value = false
      await nextTick()
      hasRules.value = true
      await nextTick()
      expect(mocks.init).not.toHaveBeenCalled()
      expect(mocks.chart.setOption).not.toHaveBeenCalled()

      active.value = true
      await nextTick()
      await nextTick()
      expect(mocks.init).toHaveBeenCalledOnce()
      expect(mocks.chart.setOption.mock.lastCall?.[0].color).toBe('rgba(29, 29, 31, 0.6)')
      expect(mocks.chart.setOption.mock.lastCall?.[0].series[0]).toEqual({
        data: [10],
        itemStyle: { color: themeColor },
      })

      active.value = false
      await nextTick()
      expect(mocks.chart.dispose).toHaveBeenCalledOnce()
      const rendersBeforeRemount = mocks.chart.setOption.mock.calls.length
      themeColor = 'rgb(240, 240, 245)'
      document.documentElement.style.colorScheme = 'dark'
      defaultTheme.value = 'dark'
      value.value = 20
      await nextTick()
      expect(mocks.chart.setOption).toHaveBeenCalledTimes(rendersBeforeRemount)

      active.value = true
      await nextTick()
      await nextTick()
      expect(mocks.init).toHaveBeenCalledTimes(2)
      expect(mocks.chart.setOption.mock.lastCall?.[0].color).toBe('rgba(245, 245, 247, 0.6)')
      expect(mocks.chart.setOption.mock.lastCall?.[0].series[0]).toEqual({
        data: [20],
        itemStyle: { color: themeColor },
      })
      document.documentElement.style.colorScheme = 'light'
      defaultTheme.value = 'light'
      await nextTick()
      await nextTick()
      expect(mocks.chart.setOption.mock.lastCall?.[0].color).toBe('rgba(29, 29, 31, 0.6)')
      expect(
        mocks.chart.setOption.mock.calls.every(
          ([option]) => option.series[0].itemStyle.color !== '',
        ),
      ).toBe(true)
    } finally {
      app.unmount()
      host.remove()
      styleSpy.mockRestore()
      document.documentElement.style.colorScheme = wasColorScheme
      defaultTheme.value = wasDefaultTheme
      autoTheme.value = wasAutoTheme
    }
  })
})
