import { isMiddleScreen } from '@/helper/utils'
import { emoji, font, theme } from '@/store/settings'
import { useElementSize } from '@vueuse/core'
import { BarChart, LineChart, SankeyChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import * as echarts from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { debounce } from 'lodash'
import type { ComputedRef, Ref } from 'vue'
import { nextTick, onMounted, onUnmounted, reactive, ref, shallowRef, watch } from 'vue'

echarts.use([
  BarChart,
  LineChart,
  SankeyChart,
  GridComponent,
  LegendComponent,
  TooltipComponent,
  CanvasRenderer,
])

export type EChart = echarts.ECharts
export type EChartOption = echarts.EChartsCoreOption

type ChartElementRef = Ref<HTMLElement | null | undefined>

export interface ChartTheme {
  primary30: string
  primary60: string
  info30: string
  info60: string
  lowLatency30: string
  lowLatency60: string
  mediumLatency30: string
  mediumLatency60: string
  highLatency30: string
  highLatency60: string
  baseContent10: string
  baseContent30: string
  baseContent60: string
  baseContent: string
  base70: string
}

const createThemeProbe = () => {
  const probe = document.createElement('span')
  probe.className =
    'border-b-primary/30 border-t-primary/60 border-l-info/30 border-r-info/60 text-base-content/10 bg-base-100/70 outline-base-content/30 decoration-base-content/60 caret-base-content'

  const latencyProbe = document.createElement('span')
  latencyProbe.className =
    'border-b-low-latency/30 border-t-low-latency/60 border-l-medium-latency/30 border-r-medium-latency/60 text-high-latency/30 bg-high-latency/60'
  probe.appendChild(latencyProbe)

  Object.assign(probe.style, {
    position: 'absolute',
    width: '0',
    height: '0',
    overflow: 'hidden',
    pointerEvents: 'none',
    visibility: 'hidden',
  })

  return { probe, latencyProbe }
}

export const useChartTheme = (chartRef: ChartElementRef) => {
  const colors = reactive<ChartTheme>({
    primary30: '',
    primary60: '',
    info30: '',
    info60: '',
    lowLatency30: '',
    lowLatency60: '',
    mediumLatency30: '',
    mediumLatency60: '',
    highLatency30: '',
    highLatency60: '',
    baseContent10: '',
    baseContent30: '',
    baseContent60: '',
    baseContent: '',
    base70: '',
  })
  const fontFamily = ref('')
  let probe: HTMLSpanElement | null = null
  let latencyProbe: HTMLSpanElement | null = null

  const update = () => {
    if (!probe || !latencyProbe) return

    const style = getComputedStyle(probe)
    colors.primary30 = style.borderBottomColor
    colors.primary60 = style.borderTopColor
    colors.info30 = style.borderLeftColor
    colors.info60 = style.borderRightColor
    colors.baseContent10 = style.color
    colors.baseContent30 = style.outlineColor
    colors.baseContent60 = style.textDecorationColor
    colors.baseContent = style.caretColor
    colors.base70 = style.backgroundColor
    fontFamily.value = style.fontFamily

    const latencyStyle = getComputedStyle(latencyProbe)
    colors.lowLatency30 = latencyStyle.borderBottomColor
    colors.lowLatency60 = latencyStyle.borderTopColor
    colors.mediumLatency30 = latencyStyle.borderLeftColor
    colors.mediumLatency60 = latencyStyle.borderRightColor
    colors.highLatency30 = latencyStyle.color
    colors.highLatency60 = latencyStyle.backgroundColor
  }

  onMounted(() => {
    const host = chartRef.value?.closest('#app-content') ?? chartRef.value?.parentElement
    if (!host) return

    const themeProbe = createThemeProbe()
    probe = themeProbe.probe
    latencyProbe = themeProbe.latencyProbe
    host.appendChild(probe)
    update()
  })

  watch([theme, font, emoji], () => nextTick(update))

  onUnmounted(() => {
    probe?.remove()
    probe = null
    latencyProbe = null
  })

  return { colors, fontFamily }
}

interface UseEChartOptions {
  paused?: Readonly<Ref<boolean>>
  isEmpty?: Readonly<Ref<boolean>>
  onInit?: (chart: EChart) => void | (() => void)
}

export const useEChart = (
  chartRef: ChartElementRef,
  options: ComputedRef<EChartOption>,
  { paused, isEmpty, onInit }: UseEChartOptions = {},
) => {
  const chart = shallowRef<EChart>()
  const { width, height } = useElementSize(chartRef)
  let removeInitListeners: (() => void) | undefined
  let touchTarget: HTMLElement | null = null

  const render = () => {
    if (!chart.value || paused?.value) return

    if (isEmpty?.value) {
      chart.value.clear()
      return
    }

    chart.value.setOption(options.value)
  }

  const resize = debounce(() => chart.value?.resize(), 100)

  const hideTooltip = () => {
    chart.value?.dispatchAction({ type: 'hideTip' })
  }

  const removeTouchListener = () => {
    touchTarget?.removeEventListener('touchend', hideTooltip)
    touchTarget = null
  }

  const syncTouchListener = () => {
    removeTouchListener()
    if (!isMiddleScreen.value || !chartRef.value) return

    touchTarget = chartRef.value
    touchTarget.addEventListener('touchend', hideTooltip)
  }

  watch(options, render)
  watch([width, height], resize)
  watch(isMiddleScreen, syncTouchListener)
  if (paused) {
    watch(paused, (value) => {
      if (!value) render()
    })
  }
  if (isEmpty) {
    watch(isEmpty, render)
  }

  onMounted(() => {
    if (!chartRef.value) return

    chart.value = echarts.init(chartRef.value)
    removeInitListeners = onInit?.(chart.value) || undefined
    syncTouchListener()
    render()
  })

  onUnmounted(() => {
    resize.cancel()
    removeTouchListener()
    removeInitListeners?.()
    chart.value?.dispose()
    chart.value = undefined
  })

  return {
    chart,
    render,
    resize: () => {
      resize.cancel()
      chart.value?.resize()
    },
  }
}

export { echarts }
