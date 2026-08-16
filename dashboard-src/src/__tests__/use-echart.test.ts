import { useEChart } from '@/composables/useEChart'
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
})
