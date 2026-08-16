import type { CSSProperties } from 'vue'
import type { ThemeColorScheme } from './theme'

export type AnsiTextSegment = {
  text: string
  style?: CSSProperties
}

type AnsiState = {
  color?: string
  backgroundColor?: string
  fontWeight?: CSSProperties['fontWeight']
  fontStyle?: CSSProperties['fontStyle']
  textDecoration?: CSSProperties['textDecoration']
  opacity?: CSSProperties['opacity']
}

type Rgb = readonly [number, number, number]

const ANSI_PATTERN = /\x1b\[([0-9;]*)m/g
const NORMAL_COLORS: Rgb[] = [
  [0, 0, 0],
  [205, 49, 49],
  [13, 188, 121],
  [229, 229, 16],
  [36, 114, 200],
  [188, 63, 188],
  [17, 168, 205],
  [229, 229, 229],
]
const BRIGHT_COLORS: Rgb[] = [
  [102, 102, 102],
  [241, 76, 76],
  [35, 209, 139],
  [245, 245, 67],
  [59, 142, 234],
  [214, 112, 214],
  [41, 184, 219],
  [255, 255, 255],
]
const BLACK: Rgb = [0, 0, 0]
const WHITE: Rgb = [255, 255, 255]
const MIN_CONTRAST_RATIO = 4.5

const mixRgb = (color: Rgb, target: Rgb, amount: number): Rgb =>
  color.map((channel, index) =>
    Math.round(channel + (target[index] - channel) * amount),
  ) as unknown as Rgb

const relativeLuminance = (color: Rgb) => {
  const [red, green, blue] = color.map((channel) => {
    const value = channel / 255
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  })

  return red * 0.2126 + green * 0.7152 + blue * 0.0722
}

const contrastRatio = (foreground: Rgb, background: Rgb) => {
  const foregroundLuminance = relativeLuminance(foreground)
  const backgroundLuminance = relativeLuminance(background)
  const lighter = Math.max(foregroundLuminance, backgroundLuminance)
  const darker = Math.min(foregroundLuminance, backgroundLuminance)

  return (lighter + 0.05) / (darker + 0.05)
}

const ensureContrast = (color: Rgb, background: Rgb, target: Rgb): Rgb => {
  if (contrastRatio(color, background) >= MIN_CONTRAST_RATIO) return color

  let low = 0
  let high = 1
  for (let i = 0; i < 12; i++) {
    const middle = (low + high) / 2
    if (contrastRatio(mixRgb(color, target, middle), background) >= MIN_CONTRAST_RATIO) {
      high = middle
    } else {
      low = middle
    }
  }

  return mixRgb(color, target, high)
}

const rgbString = ([red, green, blue]: Rgb) => `rgb(${red}, ${green}, ${blue})`

const foregroundColor = (color: Rgb, colorScheme: ThemeColorScheme) =>
  rgbString(
    ensureContrast(
      color,
      colorScheme === 'dark' ? BLACK : WHITE,
      colorScheme === 'dark' ? WHITE : BLACK,
    ),
  )

const backgroundColor = (color: Rgb, colorScheme: ThemeColorScheme) => {
  const foreground = colorScheme === 'dark' ? WHITE : BLACK
  const target = colorScheme === 'dark' ? BLACK : WHITE

  return rgbString(ensureContrast(color, foreground, target))
}

const colorFrom256 = (value: number): Rgb | undefined => {
  if (value < 0 || value > 255) return undefined
  if (value < 8) return NORMAL_COLORS[value]
  if (value < 16) return BRIGHT_COLORS[value - 8]

  if (value < 232) {
    const index = value - 16
    const red = Math.floor(index / 36)
    const green = Math.floor((index % 36) / 6)
    const blue = index % 6
    const toChannel = (channel: number) => (channel === 0 ? 0 : channel * 40 + 55)

    return [toChannel(red), toChannel(green), toChannel(blue)]
  }

  const gray = (value - 232) * 10 + 8

  return [gray, gray, gray]
}

const stateToStyle = (state: AnsiState): CSSProperties | undefined => {
  const style: CSSProperties = {}

  if (state.color) style.color = state.color
  if (state.backgroundColor) style.backgroundColor = state.backgroundColor
  if (state.fontWeight) style.fontWeight = state.fontWeight
  if (state.fontStyle) style.fontStyle = state.fontStyle
  if (state.textDecoration) style.textDecoration = state.textDecoration
  if (state.opacity) style.opacity = state.opacity

  return Object.keys(style).length ? style : undefined
}

const applyAnsiCodes = (state: AnsiState, codes: number[], colorScheme: ThemeColorScheme) => {
  for (let i = 0; i < codes.length; i++) {
    const code = codes[i]

    if (code === 0) {
      state.color = undefined
      state.backgroundColor = undefined
      state.fontWeight = undefined
      state.fontStyle = undefined
      state.textDecoration = undefined
      state.opacity = undefined
    } else if (code === 1) {
      state.fontWeight = 700
    } else if (code === 2) {
      state.opacity = 0.7
    } else if (code === 3) {
      state.fontStyle = 'italic'
    } else if (code === 4) {
      state.textDecoration = 'underline'
    } else if (code === 22) {
      state.fontWeight = undefined
      state.opacity = undefined
    } else if (code === 23) {
      state.fontStyle = undefined
    } else if (code === 24) {
      state.textDecoration = undefined
    } else if (code >= 30 && code <= 37) {
      state.color = foregroundColor(NORMAL_COLORS[code - 30], colorScheme)
    } else if (code === 39) {
      state.color = undefined
    } else if (code >= 40 && code <= 47) {
      state.backgroundColor = backgroundColor(NORMAL_COLORS[code - 40], colorScheme)
    } else if (code === 49) {
      state.backgroundColor = undefined
    } else if (code >= 90 && code <= 97) {
      state.color = foregroundColor(BRIGHT_COLORS[code - 90], colorScheme)
    } else if (code >= 100 && code <= 107) {
      state.backgroundColor = backgroundColor(BRIGHT_COLORS[code - 100], colorScheme)
    } else if ((code === 38 || code === 48) && codes[i + 1] === 5) {
      const color = colorFrom256(codes[i + 2])
      if (color && code === 38) state.color = foregroundColor(color, colorScheme)
      if (color && code === 48) state.backgroundColor = backgroundColor(color, colorScheme)
      i += 2
    } else if ((code === 38 || code === 48) && codes[i + 1] === 2) {
      const channels = codes.slice(i + 2, i + 5)
      const color =
        channels.length === 3 && channels.every((channel) => channel >= 0 && channel <= 255)
          ? (channels as unknown as Rgb)
          : undefined
      if (color && code === 38) state.color = foregroundColor(color, colorScheme)
      if (color && code === 48) state.backgroundColor = backgroundColor(color, colorScheme)
      i += 4
    }
  }
}

export const stripAnsi = (value: string) => value.replace(ANSI_PATTERN, '')

export const parseAnsiText = (value: string, colorScheme: ThemeColorScheme): AnsiTextSegment[] => {
  const segments: AnsiTextSegment[] = []
  const state: AnsiState = {}
  let cursor = 0
  let match: RegExpExecArray | null

  while ((match = ANSI_PATTERN.exec(value))) {
    if (match.index > cursor) {
      segments.push({ text: value.slice(cursor, match.index), style: stateToStyle(state) })
    }

    const codes = match[1] ? match[1].split(';').map((code) => Number(code || 0)) : [0]
    applyAnsiCodes(state, codes, colorScheme)
    cursor = match.index + match[0].length
  }

  if (cursor < value.length) {
    segments.push({ text: value.slice(cursor), style: stateToStyle(state) })
  }

  return segments.length ? segments : [{ text: value }]
}
