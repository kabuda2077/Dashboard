import { getLatencyByName, getNowProxyNodeName, proxyMap } from '@/assembly/proxies'
import { selectTopDownloads } from '@/helper/topConnections'
import { independentLatencyTest, speedtestUrl } from '@/store/settings'
import type { Connection } from '@/types'
import { describe, expect, it, vi } from 'vitest'
vi.mock('@/assembly/version', () => ({ isSingBoxCore: { value: false } }))

describe('bounded derived calculations', () => {
  it.each([100, 1000, 10000])('Top 4 matches stable full sorting for %i rows', (size) => {
    const rows = Array.from(
      { length: size },
      (_, index) => ({ id: String(index), downloadSpeed: (index * 7919) % 10007 }) as Connection,
    )
    const reference = rows
      .filter((row) => row.downloadSpeed > 0)
      .sort((a, b) => b.downloadSpeed - a.downloadSpeed)
      .slice(0, 4)
    expect(selectTopDownloads(rows, []).map((row) => row.id)).toEqual(
      reference.map((row) => row.id),
    )
  })
  it('retains a zero-speed slot only when explicitly provided by the same session', () => {
    const old = { id: 'old', downloadSpeed: 100 } as Connection
    expect(selectTopDownloads([], [old])).toEqual([{ ...old, downloadSpeed: 0 }])
    expect(selectTopDownloads([], [])).toEqual([])
  })
  it('reading a missing independent test bucket never writes into reactive state', () => {
    independentLatencyTest.value = true
    speedtestUrl.value = 'https://probe.test/'
    proxyMap.value = {
      test: { name: 'test', type: 'Shadowsocks', history: [], extra: {} } as never,
    }
    const before = JSON.stringify(proxyMap.value)
    expect(getLatencyByName('test')).toBe(0)
    expect(JSON.stringify(proxyMap.value)).toBe(before)
  })
  it('malformed cyclic proxy chains terminate instead of blocking rendering', () => {
    proxyMap.value = { a: { name: 'a', now: 'b' }, b: { name: 'b', now: 'a' } } as never
    expect(getNowProxyNodeName('a')).toBe('a')
  })
})
