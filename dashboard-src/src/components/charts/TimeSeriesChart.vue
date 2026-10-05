<template>
  <div
    class="relative h-28 w-full overflow-hidden"
  >
    <div
      ref="chartRef"
      class="h-full w-full"
    />
    <button
      class="btn btn-ghost btn-xs absolute right-1 bottom-0"
      @click="isPaused = !isPaused"
    >
      <component
        :is="isPaused ? PlayCircleIcon : PauseCircleIcon"
        class="h-4 w-4"
      />
    </button>
  </div>
</template>

<script setup lang="ts">
import { echarts, useChartTheme, useEChart, type EChartOption } from '@/composables/useEChart'
import { PauseCircleIcon, PlayCircleIcon } from '@heroicons/vue/24/outline'
import { computed, ref } from 'vue'
import type { ChartSeries, ChartTooltipParam } from './chartTypes'
import { getChartPointValue } from './chartTypes'

const props = withDefaults(
  defineProps<{
    data: ChartSeries[]
    labelFormatter: (value: number) => string
    tooltipFormatter: (value: ChartTooltipParam[]) => string
    yAxisFloor?: number
    windowSeconds?: number
  }>(),
  {
    windowSeconds: 20,
  },
)

const chartRef = ref<HTMLElement>()
const isPaused = ref(false)
const { colors, fontFamily } = useChartTheme(chartRef)

const options = computed<EChartOption>(() => {
  const lastPoint = props.data[0]?.data.at(-1)
  const latest = lastPoint ? getChartPointValue(lastPoint)[0] : Date.now()

  return {
    animationDurationUpdate: 1000,
    animationEasingUpdate: 'linear',
    legend: {
      bottom: 0,
      data: props.data.map((item) => item.name),
      textStyle: {
        color: colors.baseContent,
        fontFamily: fontFamily.value,
        fontSize: 10,
      },
    },
    grid: { left: 50, top: 15, right: 8, bottom: 25 },
    tooltip: {
      show: true,
      trigger: 'axis',
      backgroundColor: colors.base70,
      borderColor: colors.base70,
      borderRadius: 8,
      confine: true,
      padding: [0, 3],
      textStyle: {
        color: colors.baseContent,
        fontFamily: fontFamily.value,
        fontSize: 11,
      },
      formatter: props.tooltipFormatter,
    },
    xAxis: {
      type: 'time',
      min: latest - (props.windowSeconds - 1) * 1000,
      max: latest - 1000,
      axisLine: { show: false },
      axisTick: { show: false },
      splitLine: { show: false },
      axisLabel: { show: false },
    },
    yAxis: {
      type: 'value',
      splitNumber: 4,
      min: 0,
      max:
        props.yAxisFloor === undefined
          ? undefined
          : (value: { max: number }) => Math.max(value.max, props.yAxisFloor!),
      axisTick: { show: false },
      axisLine: { show: false },
      splitLine: {
        show: true,
        lineStyle: {
          type: 'dashed',
          color: colors.baseContent10,
        },
      },
      axisLabel: {
        formatter: props.labelFormatter,
        color: colors.baseContent,
        fontFamily: fontFamily.value,
        fontSize: 10,
        align: 'left',
        padding: [0, 0, 0, -35],
      },
    },
    series: props.data.map((item, index) => {
      const lineColor = index === props.data.length - 1 ? colors.primary60 : colors.info60
      const areaColor = index === props.data.length - 1 ? colors.primary30 : colors.info30

      return {
        name: item.name,
        type: 'line',
        data: item.data,
        symbol: 'none',
        smooth: true,
        color: lineColor,
        emphasis: { disabled: true },
        lineStyle: { width: 1 },
        areaStyle: {
          color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
            { offset: 0, color: lineColor },
            { offset: 1, color: areaColor },
          ]),
        },
      }
    }),
  }
})

useEChart(chartRef, options, { paused: isPaused })
</script>
