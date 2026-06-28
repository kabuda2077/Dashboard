<template>
  <div class="relative h-full w-full overflow-hidden">
    <div
      ref="chartRef"
      class="h-full w-full"
    />
    <span
      class="border-b-primary/30 border-t-primary/60 border-l-info/30 border-r-info/60 text-base-content/60 bg-base-100/70 hidden"
      ref="colorRef"
    />
    <span
      class="border-b-low-latency/30 border-t-low-latency/60 border-l-medium-latency/30 border-r-medium-latency/60 text-high-latency/30 bg-high-latency/60 hidden"
      ref="latencyColorRef"
    />
  </div>
</template>

<script setup lang="ts">
import { isMiddleScreen } from '@/helper/utils'
import { font, theme } from '@/store/settings'
import { useElementSize } from '@vueuse/core'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import * as echarts from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { debounce } from 'lodash'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

echarts.use([LineChart, GridComponent, TooltipComponent, CanvasRenderer])

const props = withDefaults(
  defineProps<{
    data: { name: number; value: number }[]
    min?: number
    color?: 'primary' | 'info' | 'lowLatency' | 'mediumLatency' | 'highLatency'
    name?: string
    showSymbols?: boolean
    labelFormatter?: (value: number) => string
    tooltipFormatter?: (value: ToolTipParams[]) => string
  }>(),
  { min: 1, color: 'primary' },
)

const chartRef = ref()
const colorRef = ref()
const latencyColorRef = ref()

const colorSet = {
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
  baseContent40: '',
  baseContent: '',
  base70: '',
}

let fontFamily = ''

const updateColorSet = () => {
  if (!colorRef.value) return
  const s = getComputedStyle(colorRef.value)
  colorSet.baseContent = s.getPropertyValue('--color-base-content').trim()
  colorSet.base70 = s.backgroundColor
  colorSet.baseContent40 = s.color
  colorSet.primary30 = s.borderBottomColor
  colorSet.primary60 = s.borderTopColor
  colorSet.info30 = s.borderLeftColor
  colorSet.info60 = s.borderRightColor

  const latencyS = getComputedStyle(latencyColorRef.value)
  colorSet.lowLatency30 = latencyS.borderBottomColor
  colorSet.lowLatency60 = latencyS.borderTopColor
  colorSet.mediumLatency30 = latencyS.borderLeftColor
  colorSet.mediumLatency60 = latencyS.borderRightColor
  colorSet.highLatency30 = latencyS.color
  colorSet.highLatency60 = latencyS.backgroundColor
}

const updateFontFamily = () => {
  if (!colorRef.value) return
  fontFamily = getComputedStyle(colorRef.value).fontFamily
}

const seriesColor = computed(() => {
  if (props.color === 'info') return colorSet.info60
  if (props.color === 'lowLatency') return colorSet.lowLatency60
  if (props.color === 'mediumLatency') return colorSet.mediumLatency60
  if (props.color === 'highLatency') return colorSet.highLatency60
  return colorSet.primary60
})
const areaColor = computed(() => {
  if (props.color === 'info') return colorSet.info30
  if (props.color === 'lowLatency') return colorSet.lowLatency30
  if (props.color === 'mediumLatency') return colorSet.mediumLatency30
  if (props.color === 'highLatency') return colorSet.highLatency30
  return colorSet.primary30
})

const options = computed(() => ({
  grid: { left: 0, top: 0, right: props.labelFormatter ? 30 : 0, bottom: 0 },
  tooltip: props.tooltipFormatter
    ? {
        show: true,
        trigger: 'axis' as const,
        backgroundColor: colorSet.base70,
        borderColor: colorSet.base70,
        confine: true,
        padding: [0, 5],
        textStyle: {
          color: colorSet.baseContent,
          fontFamily,
          fontSize: 11,
        },
        formatter: props.tooltipFormatter,
      }
    : { show: false },
  xAxis: {
    type: 'category' as const,
    show: false,
    boundaryGap: false,
  },
  yAxis: {
    type: 'value' as const,
    show: true,
    position: 'right' as const,
    splitNumber: 2,
    min: 0,
    max: (value: { max: number }) => Math.max(value.max, props.min),
    axisLine: { show: false },
    axisTick: { show: false },
    splitLine: { show: false },
    axisLabel: props.labelFormatter
      ? {
          show: true,
          inside: false,
          fontSize: 9,
          color: colorSet.baseContent40,
          fontFamily,
          margin: 4,
          formatter: (value: number) => (value === 0 ? '' : props.labelFormatter!(value)),
        }
      : { show: false },
  },
  series: [
    {
      type: 'line' as const,
      name: props.name,
      symbol: props.showSymbols ? 'circle' : 'none',
      symbolSize: 3,
      smooth: true,
      lineStyle: { width: 1.5 },
      data: props.data,
      color: seriesColor.value,
      emphasis: { disabled: true },
      areaStyle: {
        color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
          { offset: 0, color: seriesColor.value },
          { offset: 1, color: areaColor.value },
        ]),
      },
    },
  ],
}))

let myChart: echarts.ECharts | null = null
let touchEndHandler: ((e: TouchEvent) => void) | null = null

onMounted(() => {
  updateColorSet()
  updateFontFamily()
  watch(theme, updateColorSet)
  watch(font, updateFontFamily)

  myChart = echarts.init(chartRef.value)
  myChart.setOption(options.value)

  watch(options, () => {
    myChart?.setOption(options.value)
  })

  const { width } = useElementSize(chartRef)
  const resize = debounce(() => myChart?.resize(), 100)
  watch(width, resize)

  if (isMiddleScreen.value && chartRef.value) {
    touchEndHandler = () => {
      myChart?.dispatchAction({ type: 'hideTip' })
    }
    chartRef.value.addEventListener('touchend', touchEndHandler)
  }
})

onUnmounted(() => {
  if (chartRef.value && touchEndHandler) {
    chartRef.value.removeEventListener('touchend', touchEndHandler)
  }
  if (myChart) {
    myChart.dispose()
    myChart = null
  }
})
</script>
