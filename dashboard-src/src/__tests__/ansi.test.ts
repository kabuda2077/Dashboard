import { describe, expect, it } from 'vitest'
import { parseAnsiText, stripAnsi } from '@/helper/ansi'

describe('ANSI log colors', () => {
  it('keeps ANSI control sequences out of rendered text', () => {
    expect(stripAnsi('\u001b[31merror\u001b[0m')).toBe('error')
  })

  it('adjusts dark-theme ANSI foreground colors for contrast', () => {
    const [segment] = parseAnsiText('\u001b[30mmessage', 'dark')

    expect(segment.text).toBe('message')
    expect(segment.style?.color).toMatch(/^rgb\(/)
    expect(segment.style?.color).not.toBe('rgb(0, 0, 0)')
  })

  it('preserves readable ANSI colors in light themes', () => {
    const [segment] = parseAnsiText('\u001b[31merror', 'light')

    expect(segment.style?.color).toBe('rgb(205, 49, 49)')
  })
})
