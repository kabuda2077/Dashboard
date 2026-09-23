// Run only with: node node_modules/vitest/vitest.mjs run --config bench/vitest.config.ts
// Fixed synthetic fixture; no network, backend, user profiles, or wall-clock assertions.
import { describe, expect, it } from 'vitest'
import { computed } from 'vue'
import { getLatencyByName, proxyMap, proxyProviederList } from '../src/assembly/proxies'
import { version } from '../src/assembly/version'
import { useRenderProxyList } from '../src/composables/renderProxies'
import { independentLatencyTest, groupTestUrls, speedtestUrl, hideUnavailableProxies } from '../src/store/settings'
import type { Proxy } from '../src/types'

const groups = ['Group-A', 'Group-B', 'Group-C', 'Group-D']
const provider = 'Provider-1'
const urls = ['https://default.example/204', 'https://a.example/204', 'https://b.example/204']
const shared = Array.from({ length: 64 }, (_, n) => `Shared-${n}`)
const entries = groups.map((group, g) => ({
  group,
  names: [groups[(g + 1) % groups.length], ...shared, ...Array.from({ length: 63 }, (_, n) => `G${g}-${n}`)],
}))
entries.push({ group: '', names: [...shared, ...Array.from({ length: 64 }, (_, n) => `G0-${n}`)] })
const uniqueNames = [...new Set(entries.flatMap(({ names }) => names))]
const fixtureProxy = (name: string, index: number): Proxy => ({
  name, type: 'Shadowsocks', now: name, history: [{ time: 'fixture', delay: index % 11 === 0 ? 0 : 80 + index % 200 }],
  extra: Object.fromEntries(urls.map((url, u) => [url, {
    alive: true, history: [{ time: 'fixture', delay: index % 13 === 0 ? 0 : 70 + u * 100 + index % 170 }],
  }])),
  udp: true, icon: '',
})

function setup(mode: 'mihomo-independent' | 'mihomo-shared' | 'sing-box-independent') {
  speedtestUrl.value = urls[0]
  groupTestUrls.value = [{ name: groups[0], url: urls[1] }, { name: groups[1], url: urls[2] }]
  independentLatencyTest.value = mode !== 'mihomo-shared'
  version.value = mode === 'sing-box-independent' ? 'sing-box 1.0' : 'meta 1.0'
  hideUnavailableProxies.value = false
  const nodes = Object.fromEntries(uniqueNames.map((name, index) => [name, fixtureProxy(name, index)]))
  // Exercise group -> selected node traversal as well as direct nodes.
  for (const [index, group] of groups.entries()) {
    nodes[group] = { ...fixtureProxy(group, index), type: 'Selector', now: shared[index], all: entries[index].names }
  }
  proxyMap.value = nodes
  proxyProviederList.value = [{ name: provider, testUrl: urls[2], proxies: [], updatedAt: '', vehicleType: 'HTTP' }]
}

function directPass(reuse: boolean) {
  let calls = 0
  let checksum = 0
  for (const { group, names } of entries) {
    const cache = new Map<string, number>()
    for (const name of names) {
      const value = getLatencyByName(name, group || undefined)
      calls++
      cache.set(name, value)
      checksum += value
    }
    for (const name of names) {
      // The second read models proxiesCount after renderProxies has built its latency map.
      const value = reuse ? cache.get(name)! : getLatencyByName(name, group)
      if (!reuse) calls++
      checksum += value > 0 ? 1 : 0
    }
  }
  return { calls, checksum }
}

function timed(fn: () => void, iterations: number) {
  const start = performance.now()
  for (let i = 0; i < iterations; i++) fn()
  return performance.now() - start
}

describe('P9 fixed-fixture proxy latency benchmark (opt-in only)', () => {
  it.each(['mihomo-independent', 'mihomo-shared', 'sing-box-independent'] as const)('%s', (mode) => {
    setup(mode)
    const baseline = directPass(false)
    const candidate = directPass(true)
    expect(candidate.checksum).toBe(baseline.checksum)
    const observed = entries.map(({ group, names }) => useRenderProxyList(computed(() => names), group || undefined))
    expect(observed.map(({ renderProxies, proxiesCount }, index) => [renderProxies.value.length, proxiesCount.value]))
      .toEqual(entries.map(({ group, names }) => [128,
        `${names.filter((name) => getLatencyByName(name, group || undefined) > 0).length}/128`]))
    const warmup = 30
    timed(() => { directPass(false); directPass(true) }, warmup)
    const samples: { baseline: number; reuse: number }[] = []
    for (let sample = 0; sample < 5; sample++) {
      const baselineFirst = sample % 2 === 0
      const first = timed(() => { directPass(!baselineFirst) }, 100)
      const second = timed(() => { directPass(baselineFirst) }, 100)
      samples.push(baselineFirst ? { baseline: first, reuse: second } : { baseline: second, reuse: first })
    }
    const median = (values: number[]) => values.sort((a, b) => a - b)[2]
    console.log(JSON.stringify({ mode, groups: 4, providerEntries: 128, nodesPerGroup: 128,
      uniqueNames: uniqueNames.length, sharedNodes: shared.length, urls, providerWithoutGroupContext: true, repeatedReadsPerPass: 1280,
      baselineCalls: baseline.calls, reusedCalls: candidate.calls, warmup, iterationsPerSample: 100,
      samplesMs: samples, medianMs: { baseline: median(samples.map((s) => s.baseline)), reuse: median(samples.map((s) => s.reuse)) },
    }))
  })
})
