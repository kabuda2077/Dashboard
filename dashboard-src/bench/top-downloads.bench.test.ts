import { expect, it } from 'vitest'
import { selectTopDownloads } from '../src/helper/topConnections'
import type { Connection } from '../src/types'
import { writeReport } from './report'

it('measures bounded Top 4 against stable full sort at 100/1000/10000 rows', () => {
  const rounds = 30, iterations = 100
  const results = [100, 1000, 10000].map((size) => {
    const rows = Array.from({ length: size }, (_, index) => ({
      id: String(index), downloadSpeed: (index * 7919) % 10007,
    }) as Connection)
    const reference = () => rows.filter((row) => row.downloadSpeed > 0)
      .sort((a, b) => b.downloadSpeed - a.downloadSpeed).slice(0, 4)
    const bounded = () => selectTopDownloads(rows, [])
    expect(bounded().map((row) => row.id)).toEqual(reference().map((row) => row.id))
    const time = (action: () => Connection[]) => {
      const start = performance.now()
      for (let i = 0; i < iterations; i++) action()
      return (performance.now() - start) / iterations
    }
    for (let warmup = 0; warmup < 5; warmup++) { time(reference); time(bounded) }
    const samples = Array.from({ length: rounds }, (_, round) => {
      if (round % 2) {
        const boundedMs = time(bounded)
        return { fullSortMs: time(reference), boundedMs }
      }
      return { fullSortMs: time(reference), boundedMs: time(bounded) }
    })
    const median = (values: number[]) => {
      values.sort((a, b) => a - b)
      return (values[14] + values[15]) / 2
    }
    return { size, samples, medianMs: {
      fullSort: median(samples.map((sample) => sample.fullSortMs)),
      bounded: median(samples.map((sample) => sample.boundedMs)),
    } }
  })
  writeReport('top-downloads', ['src/helper/topConnections.ts', 'bench/top-downloads.bench.test.ts'], {
    rounds, iterationsPerRound: iterations, results,
    scope: 'Synthetic function microbenchmark; not application startup or memory performance.',
  })
})
