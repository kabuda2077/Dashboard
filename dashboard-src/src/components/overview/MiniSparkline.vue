<template>
  <div class="relative h-full w-full overflow-hidden">
    <div
      ref="chartRef"
      class="h-full w-full"
    />
  </div>
</template>

<script setup lang="ts">
import type { ChartPoint, ChartTooltipParam } from '@/components/charts/chartTypes'
import { getChartPointValue, isTimestampedChartPoint } from '@/components/charts/chartTypes'
import { echarts, useChartTheme, useEChart, type EChartOption } from '@/composables/useEChart'
import { timeSaved } from '@/store/overview'
import { computed, ref } from 'vue'

type SparklineColor = 'primary' | 'info' | 'lowLatency' | 'mediumLatency' | 'highLatency'

const props = withDefaults(
  defineProps<{
    data: ChartPoint[]
    min?: number
    color?: SparklineColor
    name?: string
    showSymbols?: boolean
    windowSeconds?: number
    labelFormatter?: (value: number) => string
    tooltipFormatter?: (value: ChartTooltipParam[]) => string
  }>(),
  { min: 1, color: 'primary', windowSeconds: timeSaved },
)

const chartRef = ref<HTMLElement>()
const { colors, fontFamily } = useChartTheme(chartRef)

const getSeriesColors = (color: SparklineColor) => {
  switch (color) {
    case 'info':
      return [colors.info60, colors.info30]
    case 'lowLatency':
      return [colors.lowLatency60, colors.lowLatency30]
    case 'mediumLatency':
      return [colors.mediumLatency60, colors.mediumLatency30]
    case 'highLatency':
      return [colors.highLatency60, colors.highLatency30]
    default:
      return [colors.primary60, colors.primary30]
  }
}

const options = computed<EChartOption>(() => {
  const latestPoint = props.data.at(-1)
  const isTimeSeries = latestPoint
    ? Array.isArray(latestPoint) || isTimestampedChartPoint(latestPoint)
    : false
  const latest = latestPoint ? getChartPointValue(latestPoint)[0] : Date.now()
  const [lineColor, areaColor] = getSeriesColors(props.color)
  const xAxis = isTimeSeries
    ? {
        type: 'time' as const,
        show: false,
        min: latest - (props.windowSeconds - 1) * 1000,
        max: latest - 1000,
      }
    : {
        type: 'category' as const,
        show: false,
        boundaryGap: false,
      }

  return {
    animationDurationUpdate: isTimeSeries ? 1000 : 0,
    animationEasingUpdate: 'linear',
    grid: { left: 0, top: 0, right: props.labelFormatter ? 30 : 0, bottom: 0 },
    tooltip: props.tooltipFormatter
      ? {
          show: true,
          trigger: 'axis',
          backgroundColor: colors.base70,
          borderColor: colors.base70,
          confine: true,
          padding: [0, 5],
          textStyle: {
            color: colors.baseContent,
            fontFamily: fontFamily.value,
            fontSize: 11,
          },
          formatter: props.tooltipFormatter,
        }
      : { show: false },
    xAxis,
    yAxis: {
      type: 'value',
      show: true,
      position: 'right',
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
            color: colors.baseContent60,
            fontFamily: fontFamily.value,
            margin: 4,
            formatter: (value: number) => (value === 0 ? '' : props.labelFormatter!(value)),
          }
        : { show: false },
    },
    series: [
      {
        type: 'line',
        name: props.name,
        symbol: props.showSymbols ? 'circle' : 'none',
        symbolSize: 3,
        smooth: true,
        lineStyle: { width: 1.5 },
        data: props.data,
        color: lineColor,
        emphasis: { disabled: true },
        areaStyle: {
          color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
            { offset: 0, color: lineColor },
            { offset: 1, color: areaColor },
          ]),
        },
      },
    ],
  }
})

useEChart(chartRef, options)
</script>
