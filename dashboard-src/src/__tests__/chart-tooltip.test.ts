import {
  chartTooltipRow,
  escapeChartHtml,
  formatHistoryTooltipParam,
  formatTimeSeriesTooltipParam,
} from '@/components/charts/chartTooltip'
import { describe, expect, it } from 'vitest'

describe('chart tooltip formatting', () => {
  it('escapes every HTML-sensitive value', () => {
    expect(escapeChartHtml(`<tag attr="value">Tom & Jerry's</tag>`)).toBe(
      '&lt;tag attr=&quot;value&quot;&gt;Tom &amp; Jerry&#39;s&lt;/tag&gt;',
    )

    const row = chartTooltipRow({
      color: 'red" onmouseover="alert(1)',
      label: '<img src=x onerror=alert(1)>',
      detail: '1 < 2 & 3 > 2',
    })

    expect(row).not.toContain('<img')
    expect(row).not.toContain('onmouseover="alert')
    expect(row).toContain('&lt;img src=x onerror=alert(1)&gt;')
    expect(row).toContain('red&quot; onmouseover=&quot;alert(1)')
  })

  it('omits initial history points', () => {
    expect(
      formatTimeSeriesTooltipParam(
        {
          data: { name: 0, value: [0, 0], init: true },
          seriesName: 'initial',
          color: '#000',
        },
        String,
      ),
    ).toBe('')
  })

  it('formats and escapes a history series tooltip', () => {
    const result = formatHistoryTooltipParam(
      {
        data: { name: 0, value: [0, 1024] },
        seriesName: '<memory>',
        color: '#fff',
      },
      { binary: true, suffix: '/s' },
    )

    expect(result).toContain('&lt;memory&gt;')
    expect(result).toContain('1 KiB/s')
  })
})
