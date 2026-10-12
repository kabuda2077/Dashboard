/*
 * 代理页在「大量代理组 / 节点」下的渲染基准(issue #784)。
 *
 *   pnpm build && node test/bench.mjs --groups 150 --nodes 60 --cpu 10
 *
 * 量四件事:
 *   firstPaint  首屏卡片出现的时间 —— 用户等的就是这个
 *   cards/dom   稳定后实际挂载的卡片数与 DOM 元素数 —— 懒挂载是否真的在回收
 *   scroll      从头滚到底的耗时与最差帧 —— 挂载成本摊到滚动里之后有多糙
 *   idleFrame   静置时的帧间隔(连接 WS 每秒一拍) —— 后台计算是否在拖主线程
 *
 * --cpu 是 CDP 的 CPU 限速倍率,开发机跑 1 看不出问题,用 10 / 20 才接近路由器旁边那台
 * 老桌面设备。对比改动前后就在两个分支各跑一次,取中位数比。
 */
import { parseArgs } from 'node:util'
import { sleep } from './lib/cdp.mjs'
import { startHarness } from './lib/harness.mjs'

const { values } = parseArgs({
  options: {
    groups: { type: 'string', default: '150' },
    nodes: { type: 'string', default: '60' },
    conns: { type: 'string', default: '300' },
    cpu: { type: 'string', default: '1' },
    repeat: { type: 'string', default: '3' },
    width: { type: 'string', default: '1440' },
    height: { type: 'string', default: '900' },
    url: { type: 'string' },
    dist: { type: 'string' },
    json: { type: 'boolean', default: false },
    headful: { type: 'boolean', default: false },
  },
})

const repeat = Number(values.repeat)
if (!Number.isSafeInteger(repeat) || repeat < 1)
  throw new Error('repeat must be a positive integer')
const groups = Number(values.groups)
const cpuThrottling = Number(values.cpu)
const viewport = {
  width: Number(values.width),
  height: Number(values.height),
}

const measureOnce = async (harness) => {
  const page = await harness.openProxiesPage({ cpuThrottling, viewport })
  const firstPaint = await page.waitForCards(1)

  if (firstPaint === null) {
    await page.close()
    throw new Error('等了 60s 也没有卡片出现,先确认 dist 是最新的、mock 后端正常')
  }

  const firstPaintMs = Date.now() - page.navigationStartedAt

  // 等页面彻底安静下来再量稳定态
  await sleep(3000)

  const idleFrame = await page.evaluateJson(`new Promise((resolve) => {
    const gaps = []
    let last = performance.now()
    const tick = () => {
      const now = performance.now()
      gaps.push(now - last)
      last = now
      if (gaps.length < 180) requestAnimationFrame(tick)
      else {
        gaps.sort((a, b) => a - b)
        resolve(JSON.stringify({
          p50: +gaps[Math.floor(gaps.length * 0.5)].toFixed(1),
          p95: +gaps[Math.floor(gaps.length * 0.95)].toFixed(1),
          max: +gaps[gaps.length - 1].toFixed(1),
        }))
      }
    }
    requestAnimationFrame(tick)
  })`)

  const cards = await page.groupCardCount()
  const dom = await page.domNodeCount()

  // 一路滚到底:比真人甩得快,属于压力工况
  const scroll = await page.evaluateJson(`(async () => {
    const el = document.querySelector('.overflow-y-scroll')
    const startedAt = performance.now()
    let frames = 0
    let worst = 0
    let last = performance.now()

    await new Promise((resolve) => {
      const step = () => {
        const now = performance.now()
        if (frames > 0) worst = Math.max(worst, now - last)
        last = now
        frames++
        el.scrollTop += 600
        if (el.scrollTop + el.clientHeight >= el.scrollHeight - 5 || performance.now() - startedAt > 30000) resolve()
        else requestAnimationFrame(step)
      }
      requestAnimationFrame(step)
    })

    return JSON.stringify({
      ms: Math.round(performance.now() - startedAt),
      frames,
      worstFrameMs: +worst.toFixed(1),
      scrollHeight: el.scrollHeight,
    })
  })()`)

  const domAfterScroll = await page.domNodeCount()

  await page.close()

  return {
    firstPaintMs,
    cards,
    dom,
    domAfterScroll,
    idleFrame,
    scroll,
  }
}

const median = (list) => {
  const sorted = [...list].sort((a, b) => a - b)

  return sorted[Math.floor(sorted.length / 2)]
}

const harness = await startHarness({
  groups,
  nodes: Number(values.nodes),
  connections: Number(values.conns),
  appUrl: values.url,
  distDir: values.dist,
  headless: !values.headful,
})

try {
  const runs = []

  for (let i = 0; i < repeat; i++) {
    runs.push(await measureOnce(harness))
  }

  const summary = {
    groups,
    nodes: Number(values.nodes),
    cpuThrottling,
    repeat: runs.length,
    firstPaintMs: median(runs.map((run) => run.firstPaintMs)),
    cards: median(runs.map((run) => run.cards)),
    dom: median(runs.map((run) => run.dom)),
    domAfterScroll: median(runs.map((run) => run.domAfterScroll)),
    idleFrameMaxMs: median(runs.map((run) => run.idleFrame.max)),
    scrollMs: median(runs.map((run) => run.scroll.ms)),
    worstScrollFrameMs: median(runs.map((run) => run.scroll.worstFrameMs)),
  }

  if (values.json) {
    console.log(JSON.stringify({ summary, runs }, null, 2))
  } else {
    console.log(
      `\n代理页基准 · ${groups} 组 × ${values.nodes} 节点 · CPU ×${cpuThrottling} · ${runs.length} 次取中位数\n`,
    )
    console.log(`  首屏卡片            ${summary.firstPaintMs} ms`)
    console.log(`  稳定后挂载卡片数    ${summary.cards} / ${groups}`)
    console.log(`  稳定后 DOM 元素     ${summary.dom}`)
    console.log(`  滚到底之后 DOM      ${summary.domAfterScroll}   (不回收的话会一路涨)`)
    console.log(`  静置最差帧          ${summary.idleFrameMaxMs} ms`)
    console.log(
      `  滚到底耗时          ${summary.scrollMs} ms   最差帧 ${summary.worstScrollFrameMs} ms\n`,
    )
  }
} finally {
  await harness.close()
}
