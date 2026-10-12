export type CartesianChartPoint = [number, number]

export interface TimestampedChartPoint {
  name: number
  value: CartesianChartPoint
  init?: boolean
}

export interface CategoryChartPoint {
  name: number
  value: number
}

export type ChartPoint = CartesianChartPoint | TimestampedChartPoint | CategoryChartPoint

export interface ChartSeries {
  name: string
  data: ChartPoint[]
}

export interface ChartTooltipParam {
  data: ChartPoint
  seriesName: string
  color: string
}

export const getChartPointValue = (point: ChartPoint): CartesianChartPoint => {
  if (Array.isArray(point)) return point
  if (Array.isArray(point.value)) return point.value
  return [point.name, point.value]
}

export const isTimestampedChartPoint = (point: ChartPoint): point is TimestampedChartPoint =>
  !Array.isArray(point) && Array.isArray(point.value)

export const isInitialChartPoint = (point: ChartPoint) =>
  isTimestampedChartPoint(point) && point.init === true
