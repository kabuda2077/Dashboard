import { buildRuleCountData } from '@/components/overview/ruleHitCount'
import { buildTopologyData } from '@/components/overview/topology'
import type { Rule } from '@/types'
import { describe, expect, it } from 'vitest'

const labels = {
  sourceIPAddress: 'Source',
  ruleMatch: 'Rule',
  proxyChainEntry: 'Entry',
  proxyChainExit: 'Exit',
  unknown: 'Unknown',
}

const makeRule = (payload: string, hitCount: number, missCount: number): Rule => ({
  type: 'DOMAIN',
  payload,
  proxy: 'Proxy',
  size: 0,
  uuid: payload,
  index: 0,
  extra: {
    disabled: false,
    hitAt: '',
    hitCount,
    missAt: '',
    missCount,
  },
})

describe('rule count chart data', () => {
  it('sorts by the selected count and respects the item limit', () => {
    const rules = [makeRule('low', 1, 20), makeRule('high', 10, 2)]

    expect(buildRuleCountData(rules, 'hit', 1)).toEqual([{ name: 'DOMAIN\nhigh', value: 10 }])
    expect(buildRuleCountData(rules, 'miss', 2).map((item) => item.value)).toEqual([20, 2])
    expect(rules.map((rule) => rule.payload)).toEqual(['low', 'high'])
  })
})

describe('topology chart data', () => {
  it('aggregates repeated routes while preserving their original counts', () => {
    const connection = {
      source: '127.0.0.1',
      rule: 'MATCH',
      chains: ['Node', 'Group'],
    }
    const data = buildTopologyData([connection, connection], labels)

    expect(data.nodes.map((node) => [node.layer, node.name])).toEqual([
      [0, '127.0.0.1'],
      [1, 'MATCH'],
      [2, 'Group'],
      [3, 'Node'],
    ])
    expect(data.links).toHaveLength(3)
    expect(data.links.every((link) => link.originalValue === 2)).toBe(true)
  })

  it('keeps identical names in different layers as distinct nodes', () => {
    const data = buildTopologyData([{ source: 'same', rule: 'same', chains: ['same'] }], labels)

    expect(data.nodes.map((node) => node.layer)).toEqual([0, 1, 3])
    expect(new Set(data.nodes.map((node) => node.id)).size).toBe(3)
    expect(data.links.every((link) => link.source !== link.target)).toBe(true)
  })
})
