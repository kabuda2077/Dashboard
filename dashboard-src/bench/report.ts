// happy-dom uses Vite's browser transform; obtain Node facilities from the
// actual test process rather than browser-externalized import stubs.
const { createHash } = process.getBuiltinModule('crypto')
const { mkdirSync, readFileSync, writeFileSync } = process.getBuiltinModule('fs')
const { release } = process.getBuiltinModule('os')
const { resolve } = process.getBuiltinModule('path')

// Evidence only: opt-in microbenchmarks never set wall-clock correctness thresholds.
export const writeReport = (name: string, inputs: string[], result: unknown) => {
  const directory = resolve('../.tmp/performance-v2')
  mkdirSync(directory, { recursive: true })
  const report = {
    measuredAt: new Date().toISOString(),
    runtime: process.version,
    platform: `${process.platform}/${process.arch} ${release()}`,
    inputs: Object.fromEntries(inputs.map((path) => [path,
      createHash('sha256').update(readFileSync(path)).digest('hex'),
    ])),
    result,
  }
  writeFileSync(resolve(directory, `${name}.json`), JSON.stringify(report, null, 2))
  console.log(JSON.stringify(report))
}
